using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace TtsExpander
{
    // Persistent per-player records.
    // Key: "s:<steamId>" (permanent, cross-server) or "p:<playerId>" fallback
    // when the game reports SteamID 0. pid 0 / unknown = transient, never saved.
    internal static class VoiceRegistry
    {
        public const int NumSpeakers = 904;
        private static readonly System.Random Rng = new System.Random();
        private static string _path;
        private static int _pruneDays;
        private static readonly Dictionary<string, Entry> Map = new Dictionary<string, Entry>();
        private static DateTime _lastSave = DateTime.MinValue;

        public class Entry
        {
            public int Sid = -1;
            public string Name = "?";
            public string Alias = "";
            public bool Muted;
            public int OverrideSid = -1;
            public long LastSeen;
        }

        public struct FoundPlayer
        {
            public bool Found;
            public ulong SteamId;
            public uint Pid;
            public string Display;
        }

        public static void Init(string path, int pruneDays)
        {
            _path = path;
            _pruneDays = pruneDays;
            try
            {
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path))
                    {
                        // key \t sid \t name \t alias \t muted \t override \t lastSeen
                        var p = line.Split('\t');
                        if (p.Length < 7) continue;
                        string key = p[0];
                        if (!key.StartsWith("s:") && !key.StartsWith("p:"))
                        {
                            // Legacy uint-keyed line -> p: fallback key.
                            if (!uint.TryParse(key, out _)) continue;
                            key = "p:" + key;
                        }
                        if (!int.TryParse(p[1], out int sid)) continue;
                        var e = new Entry { Sid = sid, Name = p[2], Alias = p[3] };
                        bool.TryParse(p[4], out e.Muted);
                        int.TryParse(p[5], out e.OverrideSid);
                        long.TryParse(p[6], out e.LastSeen);
                        Map[key] = e;
                    }
                }
            }
            catch (Exception ex) { Plugin.Log?.LogWarning("Registry load failed: " + ex.Message); }
            Prune();
        }

        public struct Resolved { public int Sid; public string SpokenName; public bool Muted; }

        public static Resolved Resolve(ulong steamId, uint pid, string displayName)
        {
            string key = KeyFor(steamId, pid);
            if (key == null)
                return new Resolved { Sid = PickTransient(displayName), SpokenName = displayName, Muted = false };
            if (!Map.TryGetValue(key, out Entry e))
            {
                e = new Entry { Sid = ClaimUnused(), Name = displayName, LastSeen = Now() };
                Map[key] = e;
                Save();
            }
            else
            {
                e.Name = displayName;
                e.LastSeen = Now();
            }
            int sid = e.OverrideSid >= 0 && e.OverrideSid < NumSpeakers ? e.OverrideSid : e.Sid;
            string spoken = string.IsNullOrEmpty(e.Alias) ? displayName : e.Alias;
            return new Resolved { Sid = sid, SpokenName = spoken, Muted = e.Muted };
        }

        public static string KeyFor(ulong steamId, uint pid)
        {
            if (steamId != 0) return "s:" + steamId;
            if (pid != 0 && pid != uint.MaxValue) return "p:" + pid;
            return null;
        }

        // Match a spoken/display name to a live Player object -> SteamID + PlayerId.
        public static FoundPlayer FindPlayerForName(string name)
        {
            var none = new FoundPlayer { Found = false };
            try
            {
                string q = StripIndex(name).Trim().ToLowerInvariant();
                if (q.Length == 0) return none;
                foreach (var p in UnityEngine.Object.FindObjectsOfType<NuclearOption.Networking.Player>())
                {
                    string disp;
                    try { disp = p.GetDisplayName(NuclearOption.Networking.PlayerNameContext.ChatOrLeaderboard); }
                    catch { continue; }
                    if (StripIndex(disp).Trim().ToLowerInvariant() != q) continue;
                    var fp = new FoundPlayer { Found = true, Display = disp };
                    try { fp.SteamId = p.SteamID; } catch { fp.SteamId = 0; }
                    try { fp.Pid = p.PlayerRef.PlayerId; } catch { fp.Pid = uint.MaxValue; }
                    return fp;
                }
            }
            catch (Exception ex) { Plugin.Log?.LogWarning("Player scan failed: " + ex.Message); }
            return none;
        }

        private static ulong _localId;
        private static DateTime _localCheck = DateTime.MinValue;

        // Local player's SteamID (for own-echo identification). 0 = unknown.
        public static ulong LocalSteamId()
        {
            if (_localId != 0 && (DateTime.UtcNow - _localCheck).TotalSeconds < 30) return _localId;
            _localCheck = DateTime.UtcNow;
            try
            {
                foreach (var box in UnityEngine.Object.FindObjectsOfType<ChatBox>())
                {
                    NuclearOption.Networking.Player p = null;
                    try { p = Traverse.Create(box).Field("player").GetValue<NuclearOption.Networking.Player>(); }
                    catch { continue; }
                    if (p == null) continue;
                    try
                    {
                        ulong id = p.SteamID;
                        if (id != 0) { _localId = id; return id; }
                    }
                    catch { }
                }
            }
            catch { }
            return _localId;
        }

        private static readonly Regex IndexPrefix = new Regex(@"^\[\d+\]\s*", RegexOptions.Compiled);
        private static string StripIndex(string s) => IndexPrefix.Replace(s ?? "", "");

        // One-shot sid for unknown senders (no persistence).
        public static int PickTransient(string name)
        {
            unchecked
            {
                const uint offset = 2166136261, prime = 16777619;
                uint h = offset;
                foreach (char c in (name ?? "?")) { h ^= c; h *= prime; }
                return (int)(h % (uint)NumSpeakers);
            }
        }

        private static int ClaimUnused()
        {
            var used = new HashSet<int>();
            foreach (var e in Map.Values) if (e.Sid >= 0) used.Add(e.Sid);
            if (used.Count < NumSpeakers)
            {
                int sid;
                do { sid = Rng.Next(NumSpeakers); } while (!used.Add(sid));
                return sid;
            }
            // All taken: reuse least-recently-heard.
            string oldest = null;
            long oldestSeen = long.MaxValue;
            foreach (var kv in Map)
            {
                if (oldest == null || kv.Value.LastSeen < oldestSeen)
                { oldest = kv.Key; oldestSeen = kv.Value.LastSeen; }
            }
            return oldest != null ? Map[oldest].Sid : Rng.Next(NumSpeakers);
        }

        public static string FindByName(string name)
        {
            string q = (name ?? "").Trim().ToLowerInvariant();
            if (q.Length == 0) return "";
            foreach (var kv in Map)
            {
                if ((kv.Value.Name ?? "").ToLowerInvariant() == q) return kv.Key;
                if ((kv.Value.Alias ?? "").ToLowerInvariant() == q) return kv.Key;
            }
            foreach (var kv in Map)
            {
                if ((kv.Value.Name ?? "").ToLowerInvariant().Contains(q)) return kv.Key;
            }
            return "";
        }

        public static string Describe(string key)
        {
            if (!Map.TryGetValue(key, out Entry e)) return "?";
            return key + " '" + e.Name + "' sid=" + e.Sid
                + (e.OverrideSid >= 0 ? " override=" + e.OverrideSid : "")
                + (e.Muted ? " MUTED" : "")
                + (!string.IsNullOrEmpty(e.Alias) ? " aka '" + e.Alias + "'" : "");
        }

        public static void SetOverride(string key, int sid) { if (Map.TryGetValue(key, out Entry e)) { e.OverrideSid = sid; Save(); } }
        public static void SetMute(string key, bool muted) { if (Map.TryGetValue(key, out Entry e)) { e.Muted = muted; Save(); } }
        public static bool IsMuted(string key) { return Map.TryGetValue(key, out Entry e) && e.Muted; }
        public static void SetAlias(string key, string alias) { if (Map.TryGetValue(key, out Entry e)) { e.Alias = alias ?? ""; Save(); } }

        private static long Now() => DateTime.UtcNow.Ticks;

        private static void Prune()
        {
            if (_pruneDays <= 0) return;
            long cutoff = DateTime.UtcNow.AddDays(-_pruneDays).Ticks;
            int n = 0;
            foreach (var key in new List<string>(Map.Keys))
                if (Map[key].LastSeen < cutoff) { Map.Remove(key); n++; }
            if (n > 0) { Plugin.Log?.LogInfo("Pruned " + n + " unseen players."); Save(); }
        }

        public static void Save()
        {
            try
            {
                if ((DateTime.UtcNow - _lastSave).TotalSeconds < 5) return;
                _lastSave = DateTime.UtcNow;
                var sb = new StringBuilder();
                foreach (var kv in Map)
                {
                    var e = kv.Value;
                    sb.Append(kv.Key).Append('\t').Append(e.Sid).Append('\t')
                      .Append((e.Name ?? "").Replace('\t', ' ')).Append('\t')
                      .Append((e.Alias ?? "").Replace('\t', ' ')).Append('\t')
                      .Append(e.Muted).Append('\t').Append(e.OverrideSid).Append('\t')
                      .Append(e.LastSeen).AppendLine();
                }
                File.WriteAllText(_path, sb.ToString());
            }
            catch (Exception ex) { Plugin.Log?.LogWarning("Registry save failed: " + ex.Message); }
        }
    }
}
