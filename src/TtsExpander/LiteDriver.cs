#if LITE
using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TtsExpander
{
    // Lite: no engine. Sanitize each line for the game's SAPI voice and drive
    // the game's own speech rate (PlayerSettings.chatTtsSpeed, the same int the
    // settings slider writes) from the speech backlog. Game TTS untouched.
    internal static class LiteDriver
    {
        private const float CharsPerSec = 14f;
        private static readonly Regex IndexPrefix = new Regex(@"^\[\d+\]\s*", RegexOptions.Compiled);
        private static readonly Regex TagRegex = new Regex("<.*?>", RegexOptions.Compiled);
        private static readonly Regex SpaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        private static float _backlog;
        private static float _lastPump;
        private static int _baseSpeed = 1;
        private static int _applied = 1;
        private static bool _weSet;
        private static bool _longSingle;
        private static string _localName;
        private static DateTime _localCheck = DateTime.MinValue;
        private static readonly System.Collections.Generic.List<SentText> Sent = new System.Collections.Generic.List<SentText>();

        private struct SentText { public string Text; public DateTime Time; }

        public static void NoteSent(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            lock (Sent)
            {
                DateTime now = DateTime.UtcNow;
                Sent.RemoveAll(r => (now - r.Time).TotalSeconds > 15);
                Sent.Add(new SentText { Text = message, Time = now });
            }
        }

        // Graywar-style servers echo your line back labeled sender "server"
        // with your name embedded ("server said: Name: text"). Suppress it.
        private static bool IsServerEcho(string playerName, string message)
        {
            if (!string.Equals((playerName ?? "").Trim(), "server", StringComparison.OrdinalIgnoreCase))
                return false;
            if (string.IsNullOrEmpty(message)) return false;
            lock (Sent)
            {
                DateTime now = DateTime.UtcNow;
                Sent.RemoveAll(r => (now - r.Time).TotalSeconds > 15);
                foreach (var s in Sent)
                {
                    if (s.Text.Length < 6) continue;
                    if (message.IndexOf(s.Text, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            return false;
        }

        // Prefix on ChatManager.RunTTS: clean the text, let the game speak it.
        // Game speaks "<name> said: <message>" with PlayerSettings.chatTtsSpeed.
        public static bool RunTtsPrefix(string playerName, ref string message)
        {
            try
            {
                if (!ModConfig.Enabled.Value || string.IsNullOrEmpty(message)) return true;
                if (IsServerEcho(playerName, message))
                {
                    if (ModConfig.Dbg) Plugin.Log?.LogInfo("LITE skip server echo.");
                    TtsLog.Skip("server-echo", playerName, message);
                    return false;
                }
                bool isServer = string.Equals((playerName ?? "").Trim(), "server", StringComparison.OrdinalIgnoreCase);
                if (isServer && !ModConfig.SpeakServer.Value)
                {
                    if (ModConfig.Dbg) Plugin.Log?.LogInfo("LITE skip server line.");
                    TtsLog.Skip("server", playerName, message);
                    return false;
                }
                if (ModConfig.MuteOwn.Value && IsSelf(playerName))
                {
                    if (ModConfig.Dbg) Plugin.Log?.LogInfo("LITE skip own message.");
                    return false;
                }
                string raw = message;
                string clean = message;
                if (ModConfig.StripRichText.Value) clean = TagRegex.Replace(clean, "");
                if (ModConfig.StripIndex.Value) clean = IndexPrefix.Replace(clean, "");
                clean = SpaceRegex.Replace(clean, " ").Trim();
                if (clean.Length == 0) return false;
                message = clean;
                float est = Math.Max(1, clean.Length) / CharsPerSec;
                if (est > 8f) _longSingle = true;
                _backlog += est;
                TtsLog.Spoke(playerName ?? "?", "lite", -1, _applied, raw, clean);
            }
            catch (Exception ex) { Plugin.Log?.LogWarning("Lite hook issue: " + ex.Message); }
            return true;
        }

        public static void SendPrefix(string message, bool allChat)
        {
            try { NoteSent(message); } catch { }
        }

        public static void CmdPrefix(string message, bool allChat, Mirage.INetworkPlayer sender)
        {
            try { NoteSent(message); } catch { }
        }

        public static void Pump()
        {
            try
            {
                float now = Time.time;
                float dt = now - _lastPump;
                _lastPump = now;
                if (dt < 0) dt = 0;
                if (dt > 1f) dt = 1f;
                _backlog = Math.Max(0f, _backlog - dt * Math.Max(1, _applied));
                // Game TTS master off -> nothing speaks, stay out of the way.
                bool master = true;
                try { master = PlayerSettings.chatTts; } catch { }
                if (!master)
                {
                    ModConfig.LiteStatus.Value = "OFF: enable Text To Speech in game settings";
                    return;
                }
                float threshold = ModConfig.CatchUpSec.Value;
                if (master && _backlog > threshold)
                {
                    float t = Math.Min(1f, (_backlog - threshold) / threshold);
                    float cap = _longSingle ? 3f : ModConfig.CatchUpMax.Value;
                    float maxF = Math.Max(_baseSpeed, cap);
                    int target = Math.Max(_baseSpeed, (int)Math.Round(_baseSpeed + (maxF - _baseSpeed) * t));
                    ApplySpeed(target, true);
                }
                else
                {
                    if (_backlog < 1f) _longSingle = false;
                    ApplySpeed(_baseSpeed, false);
                }
                ModConfig.LiteStatus.Value = _weSet
                    ? $"catch-up x{_applied} (backlog {_backlog:F1}s)"
                    : "idle";
            }
            catch (Exception ex) { Plugin.Log?.LogWarning("Lite pump failed: " + ex.Message); }
        }

        private static void ApplySpeed(int speed, bool speeding)
        {
            try
            {
                int cur;
                try { cur = PlayerSettings.chatTtsSpeed; }
                catch { return; }
                if (!speeding)
                {
                    if (_weSet && cur != _baseSpeed)
                        PlayerSettings.chatTtsSpeed = _baseSpeed;
                    _weSet = false;
                    _baseSpeed = PlayerSettings.chatTtsSpeed;
                    _applied = _baseSpeed;
                    return;
                }
                _applied = speed;
                if (cur != speed)
                {
                    PlayerSettings.chatTtsSpeed = speed;
                    _weSet = true;
                    if (ModConfig.Dbg)
                        Plugin.Log?.LogInfo($"LITE catch-up: rate {speed} (backlog {_backlog:F1}s).");
                }
            }
            catch { }
        }

        private static bool IsSelf(string playerName)
        {
            try
            {
                if ((DateTime.UtcNow - _localCheck).TotalSeconds > 30 || _localName == null)
                {
                    _localCheck = DateTime.UtcNow;
                    _localName = null;
                    foreach (var box in UnityEngine.Object.FindObjectsOfType<ChatBox>())
                    {
                        NuclearOption.Networking.Player p = null;
                        try { p = HarmonyLib.Traverse.Create(box).Field("player").GetValue<NuclearOption.Networking.Player>(); }
                        catch { continue; }
                        if (p == null) continue;
                        try { _localName = p.GetDisplayName(NuclearOption.Networking.PlayerNameContext.ChatOrLeaderboard); break; }
                        catch { }
                    }
                }
                if (_localName == null) return false;
                string a = IndexPrefix.Replace(playerName ?? "", "").Trim().ToLowerInvariant();
                string b = IndexPrefix.Replace(_localName, "").Trim().ToLowerInvariant();
                return a.Length > 0 && a == b;
            }
            catch { return false; }
        }
    }
}
#endif
