using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace TtsExpander
{
    // Speech audit log: raw chat in, exact Piper input out (plus skips).
    // One line per item, pruned to LogDays on startup. For long test sessions.
    internal static class TtsLog
    {
        private static string _path;
        private static int _days;
        private static readonly object Gate = new object();

        public static void Init(string path, int days, string header)
        {
            _path = path;
            _days = days;
            if (days <= 0) return;
            try
            {
                if (File.Exists(path))
                {
                    DateTime cutoff = DateTime.UtcNow.AddDays(-days);
                    var keep = new List<string>();
                    foreach (var line in File.ReadAllLines(path))
                    {
                        if (line.Length > 21 && line[0] == '['
                            && DateTime.TryParseExact(line.Substring(1, 19), "yyyy-MM-dd HH:mm:ss",
                                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime at))
                        {
                            if (at < cutoff) continue;
                        }
                        keep.Add(line);
                    }
                    File.WriteAllLines(path, keep);
                }
                File.AppendAllText(path,
                    $"===== {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} {header} =====\n");
            }
            catch (Exception ex) { Plugin.Log?.LogWarning("TTS log prune failed: " + ex.Message); }
        }

        public static void Spoke(string speaker, string key, int sid, float speed, string raw, string said)
        {
            if (_days <= 0) return;
            Write($"{Stamp()} {speaker} {key} sid={sid} spd={speed:F2} | RAW '{OneLine(raw)}' | SAID '{OneLine(said)}'");
        }

        public static void Skip(string reason, string speaker, string raw)
        {
            if (_days <= 0) return;
            Write($"{Stamp()} SKIP({reason}) {speaker} | RAW '{OneLine(raw)}'");
        }

        private static string Stamp() => "[" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "]";

        private static string OneLine(string s) => (s ?? "").Replace("\r", " ").Replace("\n", " ");

        private static void Write(string line)
        {
            try
            {
                lock (Gate) File.AppendAllText(_path, line + "\n");
            }
            catch { }
        }
    }
}
