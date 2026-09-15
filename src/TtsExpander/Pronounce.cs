using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace TtsExpander
{
    // Pronunciation stage: canonical word -> speakable form. Runs AFTER
    // Expansions (autocorrect). Same whole-word mechanism, separate file so
    // tune-bench winners land here, not mixed with typo fixes.
    internal static class Pronounce
    {
        private static Regex _regex;
        private static Dictionary<string, string> _map;

        private static readonly string[] Starter = new[]
        {
            "stratolance=>Strato-lance",
            "linebreaker=>line breaker",
            "boltstrike=>bolt-strike",
            "hexhound=>hex hound",
            "chicane=>chikane",
            "alkyon=>alk yon",
            "arad=>A rad",
            "ashm=>ash-m",
            "spaag=>spag",
            "cram=>c-ram",
            "mlrs=>M L R S",
            "bvr=>B V R",
            "rcs=>R C S",
            "ins=>I N S",
        };

        public static void Init(string path)
        {
            try
            {
                if (!File.Exists(path))
                    File.WriteAllLines(path, Starter);
                Load(path);
            }
            catch (Exception ex) { Plugin.Log?.LogWarning("Pronounce init failed: " + ex.Message); }
        }

        private static void Load(string path)
        {
            _map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int i = line.IndexOf("=>", StringComparison.Ordinal);
                if (i <= 0) continue;
                string from = line.Substring(0, i).Trim();
                string to = line.Substring(i + 2).Trim();
                if (from.Length > 0 && to.Length > 0) _map[from] = to;
            }
            var words = new List<string>(_map.Keys);
            words.Sort((a, b) => b.Length.CompareTo(a.Length));
            string pattern = @"\b(" + string.Join("|", words.ConvertAll(Regex.Escape)) + @")\b";
            _regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            Plugin.Log?.LogInfo("Pronounce loaded: " + _map.Count + " entries.");
        }

        public static string Apply(string text)
        {
            if (_regex == null || string.IsNullOrEmpty(text)) return text;
            try { return _regex.Replace(text, m => _map[m.Value]); }
            catch { return text; }
        }
    }
}
