using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Mirage;
using NuclearOption.Chat;
using NuclearOption.Networking;
using System;
using System.IO;
using UnityEngine;

namespace TtsExpander
{
#if LITE
    [BepInPlugin("com.MrNoHands.tts-lite", "TTS Lite", "1.0.0")]
#else
    [BepInPlugin("com.MrNoHands.tts-expander", "TTS Expander", "1.0.0")]
#endif
    public class Plugin : BaseUnityPlugin
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
#if LITE
            Log.LogInfo("TTS Lite v1.0.0 starting.");
#else
            Log.LogInfo("TTS Expander v1.0.0 starting.");
#endif

            ModConfig.Bind(Config);
#if LITE
            string liteDir = Path.Combine(Paths.ConfigPath, "Com.MrNoHands.TtsLite");
            Directory.CreateDirectory(liteDir);
            TtsLog.Init(Path.Combine(liteDir, "tts-log.txt"), ModConfig.LogDays.Value,
                $"TTS Lite v1.0.0 Enabled={ModConfig.Enabled.Value} CatchUpSec={ModConfig.CatchUpSec.Value} CatchUpMax={ModConfig.CatchUpMax.Value}");
            try
            {
                var harmony = new Harmony("com.MrNoHands.tts-lite");
                harmony.Patch(AccessTools.Method(typeof(ChatManager), "RunTTS"),
                    prefix: new HarmonyMethod(typeof(LiteDriver).GetMethod(nameof(LiteDriver.RunTtsPrefix))));
                harmony.Patch(AccessTools.Method(typeof(ChatManager), "SendChatMessage"),
                    prefix: new HarmonyMethod(typeof(LiteDriver).GetMethod(nameof(LiteDriver.SendPrefix))));
                harmony.Patch(AccessTools.Method(typeof(ChatManager), "CmdSendChatMessage"),
                    prefix: new HarmonyMethod(typeof(LiteDriver).GetMethod(nameof(LiteDriver.CmdPrefix))));
                Log.LogInfo("Patched RunTTS (sanitize + speed driver).");
            }
            catch (Exception ex)
            {
                Log.LogError("Hook failed: " + ex);
            }
            Application.onBeforeRender += LiteDriver.Pump;
#else
            string dir = Path.Combine(Paths.ConfigPath, "Com.MrNoHands.TtsExpander");
            Directory.CreateDirectory(dir);
            VoiceRegistry.Init(Path.Combine(dir, "players.json"), ModConfig.PruneDays.Value);
            Expansions.Init(Path.Combine(dir, "expansions.txt"));
            Pronounce.Init(Path.Combine(dir, "pronounce.txt"));
            TtsLog.Init(Path.Combine(dir, "tts-log.txt"), ModConfig.LogDays.Value,
                $"TTS Expander v1.0.0 Enabled={ModConfig.Enabled.Value} Speed={ModConfig.SpeedBase.Value} Announce={ModConfig.Announce.Value}");

            try
            {
                var harmony = new Harmony("com.MrNoHands.tts-expander");
                harmony.Patch(AccessTools.Method(typeof(ChatManager), "TargetReceiveMessage"),
                    prefix: new HarmonyMethod(typeof(ChatHooks).GetMethod(nameof(ChatHooks.ReceivePrefix))));
                harmony.Patch(AccessTools.Method(typeof(ChatManager), "RunTTS"),
                    prefix: new HarmonyMethod(typeof(ChatHooks).GetMethod(nameof(ChatHooks.RunTtsPrefix))));
                harmony.Patch(AccessTools.Method(typeof(ChatManager), "SendChatMessage"),
                    prefix: new HarmonyMethod(typeof(ChatHooks).GetMethod(nameof(ChatHooks.SendPrefix))));
                // ChatBox can also send via the network command directly, bypassing SendChatMessage.
                harmony.Patch(AccessTools.Method(typeof(ChatManager), "CmdSendChatMessage"),
                    prefix: new HarmonyMethod(typeof(ChatHooks).GetMethod(nameof(ChatHooks.CmdPrefix))));
                harmony.Patch(AccessTools.Method(typeof(NuclearOption.UI.LeaderBoardRightClickMenu), "OnShowPanel"),
                    postfix: new HarmonyMethod(typeof(ScoreboardMenu).GetMethod(nameof(ScoreboardMenu.OnMenuShown))));
                Log.LogInfo("Patched receive + RunTTS + send + menu hooks.");
            }
            catch (Exception ex)
            {
                Log.LogError("Hook failed (game safe, TTS idle): " + ex);
            }

            var go = new GameObject("TtsExpanderDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            TtsDriver.Init(source);
            Application.onBeforeRender += TtsDriver.Pump;
            Log.LogInfo("Driver pump registered on onBeforeRender.");

            if (ModConfig.Enabled.Value)
                Log.LogWarning("TTS Expander ON: game default TTS is suppressed while enabled. "
                    + "Turn this mod off in F1 to get game TTS back (running both = double audio).");
#endif
        }
    }

    internal static class ModConfig
    {
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<int> LogLevel;
        public static ConfigEntry<float> CatchUpSec;
        public static ConfigEntry<int> LogDays;
#if LITE
        public static ConfigEntry<float> CatchUpMax;
        public static ConfigEntry<bool> StripRichText;
        public static ConfigEntry<bool> StripIndex;
        public static ConfigEntry<bool> MuteOwn;
        public static ConfigEntry<bool> SpeakServer;
        public static ConfigEntry<string> LiteStatus;
#else
        public static ConfigEntry<bool> SpeakOwn;
        public static ConfigEntry<int> AnnouncerSid;
        public static ConfigEntry<bool> Announce;
        public static ConfigEntry<float> SpeedBase;
        public static ConfigEntry<float> Volume;
        public static ConfigEntry<float> Noise;
        public static ConfigEntry<float> Variation;
        public static ConfigEntry<float> Pauses;
        public static ConfigEntry<int> PruneDays;
        public static ConfigEntry<string> ModelDir;
        public static ConfigEntry<int> OwnSid;
#endif
        // Debug trace (per-message hook/queue lines + heartbeat). Errors always log.
        public static bool Dbg => LogLevel != null && LogLevel.Value >= 2;

        public static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("General", "Enabled", true, "Master switch.");
            LogLevel = cfg.Bind("General", "LogLevel", 1, new ConfigDescription("0 = errors only, 1 = normal, 2 = debug trace.", new AcceptableValueRange<int>(0, 2)));
            CatchUpSec = cfg.Bind("Queue", "CatchUpSec", 3.0f, new ConfigDescription("Backlog seconds before speed ramps up.", new AcceptableValueRange<float>(2f, 30f)));
            LogDays = cfg.Bind("Storage", "LogDays", 7, new ConfigDescription("Keep TTS speech log this many days (0 = off).", new AcceptableValueRange<int>(0, 30)));
#if LITE
            CatchUpMax = cfg.Bind("Queue", "CatchUpMax", 2.0f, new ConfigDescription("Speed cap while catching up.", new AcceptableValueRange<float>(1f, 3f)));
            StripRichText = cfg.Bind("Fixes", "StripRichText", true, "Remove color/tag markup before speech.");
            StripIndex = cfg.Bind("Fixes", "StripIndex", true, "Remove [N] player-index prefixes before speech.");
            MuteOwn = cfg.Bind("General", "MuteOwn", false, "Don't speak my own messages.");
            LiteStatus = cfg.Bind("General", "Status", "", "Live driver state (read-only).");
            SpeakServer = cfg.Bind("General", "SpeakServer", true, "Speak lines from sender 'server' (notices, relays).");
#else
            SpeakOwn = cfg.Bind("General", "SpeakOwnDev", false, "Also speak my own messages.");
            Announce = cfg.Bind("Voice", "AnnounceNames", true, "Speak '<name> says:' before each message.");
            AnnouncerSid = cfg.Bind("Voice", "AnnouncerSid", 1, new ConfigDescription("Voice for the '<name> says:' prefix.", new AcceptableValueRange<int>(0, 903)));
            SpeedBase = cfg.Bind("Voice", "Speed", 1.0f, new ConfigDescription("Base speaking speed.", new AcceptableValueRange<float>(0.5f, 2f)));
            Volume = cfg.Bind("Voice", "Volume", 1.0f, new ConfigDescription("Output gain (own stage, 2 = 200%).", new AcceptableValueRange<float>(0f, 2f)));
            Noise = cfg.Bind("Voice", "Noise", 0.5f, new ConfigDescription("Phoneme randomness (lower = steadier).", new AcceptableValueRange<float>(0f, 1.5f)));
            Variation = cfg.Bind("Voice", "Variation", 1.0f, new ConfigDescription("Expressiveness variation.", new AcceptableValueRange<float>(0f, 1.5f)));
            Pauses = cfg.Bind("Voice", "Pauses", 1.0f, new ConfigDescription("Pause length at punctuation.", new AcceptableValueRange<float>(0f, 1f)));
            PruneDays = cfg.Bind("Storage", "PruneDays", 1825, new ConfigDescription("Forget players unseen this many days (0 = never).", new AcceptableValueRange<int>(0, 3650)));
            ModelDir = cfg.Bind("Storage", "ModelDir", "", "Piper model folder. Empty = <plugin>/data.");
            OwnSid = cfg.Bind("Voice", "OwnSid", -1, new ConfigDescription("Your own voice (-1 = auto).", new AcceptableValueRange<int>(-1, 903)));
#endif
        }
    }

#if !LITE
    public static class ChatHooks
    {
        // Fires for every received chat line (others). Never skips the original (display untouched).
        public static void ReceivePrefix(INetworkPlayer _, string message, Player player, bool allChat)
        {
            try
            {
                if (ModConfig.Dbg)
                    Plugin.Log?.LogInfo($"HOOK receive: player={(player == null ? "null" : "ok")} msg='{message}'");
                if (!ModConfig.Enabled.Value || player == null || string.IsNullOrEmpty(message)) return;
                uint pid;
                try { pid = player.PlayerRef.PlayerId; }
                catch { pid = uint.MaxValue; }
                ulong steamId = 0;
                try { steamId = player.SteamID; }
                catch { steamId = 0; }
                string display;
                try { display = player.GetDisplayName(PlayerNameContext.ChatOrLeaderboard); }
                catch { display = "Someone"; }
                TtsDriver.OfferSpeak(steamId, pid, display ?? "Someone", message);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Receive hook skipped line: " + ex.Message);
            }
        }

        // The game's own speak trigger fires reliably for every spoken line (including
        // incoming chat that never passes through TargetReceiveMessage on clients).
        // While enabled we speak it ourselves (with stable voices) and suppress SAPI.
        public static bool RunTtsPrefix(string playerName, string message)
        {
            try
            {
                if (ModConfig.Dbg)
                    Plugin.Log?.LogInfo($"HOOK runtts: name='{playerName}' msg='{message}' (suppressed={ModConfig.Enabled.Value})");
                if (!ModConfig.Enabled.Value) return true;
                if (string.IsNullOrEmpty(message)) return false;
                TtsDriver.OfferSpeakFromGame(playerName ?? "Someone", message);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("RunTTS hook issue: " + ex.Message);
            }
            return false;
        }

        // Outgoing chat: swallow /t commands, optionally echo own lines for testing.
        // Both SendChatMessage and CmdSendChatMessage are hooked (ChatBox may use either);
        // dedupe prevents double-handling when both fire for one line.
        private static string _lastMsg;
        private static DateTime _lastT = DateTime.MinValue;
        private static bool _lastSwallow;

        private static bool HandleOutgoing(string message, string via)
        {
            if (ModConfig.Dbg)
                Plugin.Log?.LogInfo($"HOOK send({via}): msg='{message}'");
            if (message == _lastMsg && (DateTime.UtcNow - _lastT).TotalSeconds < 2)
                return _lastSwallow;
            _lastMsg = message;
            _lastT = DateTime.UtcNow;
            _lastSwallow = false;
            try
            {
                if (!ModConfig.Enabled.Value) return false;
                if (ChatCommands.TryHandle(message)) { _lastSwallow = true; return true; }
                TtsDriver.NoteSent(message);
                if (ModConfig.SpeakOwn.Value)
                    TtsDriver.Enqueue(0, 0, "You", message, true, true);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Send hook issue: " + ex.Message);
            }
            return false;
        }

        public static bool SendPrefix(string message, bool allChat)
        {
            return !HandleOutgoing(message, "send");
        }

        public static bool CmdPrefix(string message, bool allChat, INetworkPlayer sender)
        {
            return !HandleOutgoing(message, "cmd");
        }
    }
#endif
}
