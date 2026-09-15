using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace TtsExpander
{
    // Word -> spoken form, whole-word match only, applied to speech (chat display untouched).
    internal static class Expansions
    {
        private static Regex _regex;
        private static Dictionary<string, string> _map;

        private static readonly string[] Starter = new[]
        {
            "im=>I am", "u=>you", "ur=>your", "urs=>yours", "plz=>please", "pls=>please",
            "thx=>thanks", "ty=>thank you", "rgr=>roger", "wilco=>will comply",
            "ima=>I'm going to", "imma=>I'm going to",
            "enmy=>enemy", "def=>defend", "atk=>attack", "arty=>artillery",
            "sam=>S A M", "aaa=>triple A", "aa=>A A", "afv=>A F V", "mbt=>M B T",
            "apc=>A P C", "bdf=>B D F", "pala=>pah lah", "ir=>I R",
            "fml=>fuck my life", "kys=>kill yourself",
            "brb=>be right back", "afk=>away from keyboard", "gg=>good game",
            "stratolance=>Strato-lance",
        };

        public static void Init(string path)
        {
            try
            {
                if (!File.Exists(path))
                    File.WriteAllLines(path, Starter);
                Load(path);
            }
            catch (Exception ex) { Plugin.Log?.LogWarning("Expansions init failed: " + ex.Message); }
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
            Plugin.Log?.LogInfo("Expansions loaded: " + _map.Count + " entries.");
        }

        public static string Apply(string text)
        {
            if (_regex == null || string.IsNullOrEmpty(text)) return text;
            try { return _regex.Replace(text, m => _map[m.Value]); }
            catch { return text; }
        }
    }
}
