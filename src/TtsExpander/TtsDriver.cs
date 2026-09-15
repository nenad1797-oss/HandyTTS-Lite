using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace TtsExpander
{
    // Queue pump, driven by Application.onBeforeRender (static, no MonoBehaviour Update):
    // lazy synth on dequeue (fresh settings), prefetch-one-ahead, gapless playback.
    public static class TtsDriver
    {
        private const float CharsPerSec = 14f;
        private const float MaxSpeed = 1.5f;
        private const double AnnounceGapSec = 20.0;

        private struct Item
        {
            public ulong SteamId;
            public uint Pid;
            public string Display;
            public string Message;
            public bool IsSelf;
            public bool NoAnnounce;
            public DateTime Enqueued;
        }

        private struct ReadyClip { public Item Item; public float[] Samples; public int Rate; }

        private static readonly Queue<Item> Queue = new Queue<Item>();
        private static readonly object QueueLock = new object();
        private static readonly ConcurrentQueue<Action> Main = new ConcurrentQueue<Action>();
        private static readonly List<RecentText> Recent = new List<RecentText>();

        private struct RecentText { public string Text; public DateTime Time; }

        // Central gate for anything the game wants spoken (receive path + RunTTS path):
        // same text within 3s is an echo/double-fire, speak once.
        public static void OfferSpeak(ulong steamId, uint pid, string display, string message)
        {
            lock (QueueLock)
            {
                DateTime now = DateTime.UtcNow;
                Recent.RemoveAll(r => (now - r.Time).TotalSeconds > 5);
                foreach (var r in Recent)
                    if (r.Text == message && (now - r.Time).TotalSeconds < 3)
                    {
                        if (ModConfig.Dbg)
                            Plugin.Log?.LogInfo($"QUEUE skip dup: '{message}'");
                        return;
                    }
                Recent.Add(new RecentText { Text = message, Time = now });
                Queue.Enqueue(new Item { SteamId = steamId, Pid = pid, Display = display, Message = message, IsSelf = false, Enqueued = now });
            }
            if (ModConfig.Dbg)
                Plugin.Log?.LogInfo($"QUEUE +1 (steam={steamId} pid={pid} name='{display}'): '{message}'");
        }

        public static void OfferSpeakFromGame(string playerName, string message)
        {
            if (IsEcho(message))
            {
                if (ModConfig.Dbg)
                    Plugin.Log?.LogInfo($"QUEUE skip echo: '{message}'");
                return;
            }
            var fp = VoiceRegistry.FindPlayerForName(playerName);
            ulong local = VoiceRegistry.LocalSteamId();
            if (local != 0 && fp.Found && fp.SteamId == local)
            {
                if (ModConfig.Dbg)
                    Plugin.Log?.LogInfo($"QUEUE skip own echo (identity): '{message}'");
                return;
            }
            bool noAnn = string.Equals((playerName ?? "").Trim(), "server", StringComparison.OrdinalIgnoreCase);
            lock (QueueLock)
            {
                DateTime now = DateTime.UtcNow;
                Recent.RemoveAll(r => (now - r.Time).TotalSeconds > 5);
                Recent.Add(new RecentText { Text = message, Time = now });
                Queue.Enqueue(new Item
                {
                    SteamId = fp.Found ? fp.SteamId : 0,
                    Pid = fp.Found ? fp.Pid : uint.MaxValue,
                    Display = fp.Found ? fp.Display : playerName,
                    Message = message,
                    IsSelf = false,
                    NoAnnounce = noAnn,
                    Enqueued = now
                });
            }
            if (ModConfig.Dbg)
                Plugin.Log?.LogInfo($"QUEUE +1 (steam={(fp.Found ? fp.SteamId.ToString() : "?")} name='{playerName}'): '{message}'");
        }

        private static readonly List<RecentText> Sent = new List<RecentText>();

        public static void NoteSent(string message)
        {
            lock (QueueLock)
            {
                DateTime now = DateTime.UtcNow;
                Recent.RemoveAll(r => (now - r.Time).TotalSeconds > 5);
                Sent.RemoveAll(r => (now - r.Time).TotalSeconds > 15);
                if (!string.IsNullOrEmpty(message))
                {
                    Recent.Add(new RecentText { Text = message, Time = now });
                    Sent.Add(new RecentText { Text = message, Time = now });
                }
            }
        }

        // Own line echoed back by the server (contains our sent text) -> skip.
        private static bool IsEcho(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            lock (QueueLock)
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

        private static AudioSource _source;
        private static AudioClip _clip;
        private static float _clipEndsAt;
        private static bool _synthing;
        private static ReadyClip? _prefetched;
        private static float _lastBeat;
        private static string _lastAnnouncedKey = "";
        private static DateTime _lastAnnounceTime = DateTime.MinValue;

        public static void Init(AudioSource source)
        {
            _source = source;
            Plugin.Log?.LogInfo("HEARTBEAT driver Init: pump source ready.");
        }

        public static void Enqueue(ulong steamId, uint pid, string display, string message, bool allChat, bool isSelf)
        {
            lock (QueueLock) Queue.Enqueue(new Item { SteamId = steamId, Pid = pid, Display = display, Message = message, IsSelf = isSelf, Enqueued = DateTime.UtcNow });
            if (ModConfig.Dbg)
                Plugin.Log?.LogInfo($"QUEUE +1 (steam={steamId} pid={pid} name='{display}'): '{message}'");
        }

        public static void SpeakLocal(string text)
        {
            Enqueue(0, 0, "TTS Expander", text, true, true);
        }

        // Runs on the main thread, every rendered frame (menu + mission).
        public static void Pump()
        {
            try
            {
            if (Time.time - _lastBeat > 10f)
            {
                _lastBeat = Time.time;
                if (ModConfig.Dbg)
                {
                    int n;
                    lock (QueueLock) n = Queue.Count;
                    Plugin.Log?.LogInfo($"HEARTBEAT pump ticking (queue={n}, synthing={_synthing}).");
                }
            }
            while (Main.TryDequeue(out Action a))
                {
                    try { a(); } catch (Exception ex) { Plugin.Log?.LogWarning("Main-thread job failed: " + ex.Message); }
                }
                bool idle = _clip == null || Time.time >= _clipEndsAt;
                if (_prefetched != null)
                {
                    if (idle)
                    {
                        var p = _prefetched.Value;
                        _prefetched = null;
                        Dequeue(p.Item);
                        Play(p);
                    }
                }
                else if (!_synthing)
                {
                    Item? next = Peek();
                    if (next != null && (idle || QueueDepth() > 0))
                        BeginSynth(next.Value, !idle);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Pump failed: " + ex.Message);
            }
        }

        private static int QueueDepth()
        {
            lock (QueueLock) return Queue.Count;
        }

        private static Item? Peek()
        {
            lock (QueueLock) return Queue.Count > 0 ? Queue.Peek() : (Item?)null;
        }

        private static float BacklogSec(float speedNow)
        {
            float total = 0f;
            if (_clip != null && Time.time < _clipEndsAt)
                total += _clipEndsAt - Time.time;
            lock (QueueLock)
                foreach (var it in Queue)
                    total += Math.Max(1, (it.Message ?? "").Length) / CharsPerSec / speedNow;
            return total;
        }

        private static float SpeedForNext()
        {
            float baseSpeed = ModConfig.SpeedBase.Value;
            float backlog = BacklogSec(baseSpeed);
            float threshold = ModConfig.CatchUpSec.Value;
            if (backlog <= threshold) return baseSpeed;
            float t = Math.Min(1f, (backlog - threshold) / threshold);
            return Math.Min(MaxSpeed, baseSpeed + (MaxSpeed - baseSpeed) * t);
        }

        private static void BeginSynth(Item item, bool prefetch)
        {
            _synthing = true;
            float speed = SpeedForNext();
            // Long single message: boost up to 2x even with empty queue, so a
            // 150-char keyboard smash doesn't take forever (backlog cap stays 1.5).
            float estLen = Math.Max(1, (item.Message ?? "").Length) / CharsPerSec;
            if (estLen > 8f)
            {
                float boost = 1f + Math.Min(1f, (estLen - 8f) / 12f);
                if (boost > speed) speed = Math.Min(2f, boost);
            }
            Task.Run(() =>
            {
                try
                {
                    var res = VoiceRegistry.Resolve(item.SteamId, item.Pid, item.Display);
                    int msgSid = res.Sid;
                    if (item.IsSelf && ModConfig.OwnSid.Value >= 0 && ModConfig.OwnSid.Value < VoiceRegistry.NumSpeakers)
                        msgSid = ModConfig.OwnSid.Value;
                    if (res.Muted && !item.IsSelf)
                    {
                        TtsLog.Skip("muted", item.Display, item.Message);
                        Main.Enqueue(() => { Dequeue(item); _synthing = false; });
                        return;
                    }
                    string clean = TtsSanitizer.StripRichText(item.Message);
                    clean = Expansions.Apply(clean);
                    clean = Pronounce.Apply(clean);
                    if (string.IsNullOrWhiteSpace(clean))
                    {
                        TtsLog.Skip("empty", item.Display, item.Message);
                        Main.Enqueue(() => { Dequeue(item); _synthing = false; });
                        return;
                    }
                    float[] samples;
                    string spokenName = StripIndex(res.SpokenName);
                    if (ShouldAnnounce(item))
                    {
                        var a = TtsEngine.Synth(spokenName + " says:", ModConfig.AnnouncerSid.Value, speed,
                            ModConfig.Pauses.Value, ModConfig.Noise.Value, ModConfig.Variation.Value);
                        var m = TtsEngine.Synth(clean, msgSid, speed,
                            ModConfig.Pauses.Value, ModConfig.Noise.Value, ModConfig.Variation.Value);
                        if (a == null || m == null) throw new Exception("engine not ready");
                        samples = new float[a.Length + m.Length];
                        Array.Copy(a, samples, a.Length);
                        Array.Copy(m, 0, samples, a.Length, m.Length);
                    }
                    else
                    {
                        samples = TtsEngine.Synth(clean, msgSid, speed,
                            ModConfig.Pauses.Value, ModConfig.Noise.Value, ModConfig.Variation.Value)
                            ?? throw new Exception("engine not ready");
                    }
                    TtsLog.Spoke(item.Display, KeyOf(item), msgSid, speed, item.Message, clean);
                    samples = ApplyGain(Trim(samples), ModConfig.Volume.Value);
                    var ready = new ReadyClip { Item = item, Samples = samples, Rate = TtsEngine.SampleRate };
                    Main.Enqueue(() =>
                    {
                        if (prefetch) { _prefetched = ready; }
                        else { Dequeue(item); Play(ready); }
                        _synthing = false;
                    });
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning("Synth failed, line dropped from speech (chat untouched): " + ex.Message);
                    Main.Enqueue(() => { Dequeue(item); _synthing = false; });
                }
            });
        }

        // Name the speaker only for a new voice, after a 20s gap, or for our own lines.
        private static bool ShouldAnnounce(Item item)
        {
            if (!ModConfig.Announce.Value || item.NoAnnounce) return false;
            string key = item.SteamId != 0 ? "s" + item.SteamId
                : (item.Pid != 0 && item.Pid != uint.MaxValue) ? "p" + item.Pid
                : "n" + (item.Display ?? "?");
            if (item.IsSelf || key != _lastAnnouncedKey
                || (DateTime.UtcNow - _lastAnnounceTime).TotalSeconds > AnnounceGapSec)
            {
                _lastAnnouncedKey = key;
                _lastAnnounceTime = DateTime.UtcNow;
                return true;
            }
            return false;
        }

        private static void Dequeue(Item item)
        {
            lock (QueueLock)
            {
                if (Queue.Count > 0) Queue.Dequeue();
                if (_prefetched != null && _prefetched.Value.Item.Enqueued == item.Enqueued && _prefetched.Value.Item.Pid == item.Pid)
                    _prefetched = null;
            }
        }

        private static void Play(ReadyClip ready)
        {
            try
            {
                EnsureAudioSource();
                if (_clip != null) { UnityEngine.Object.Destroy(_clip); _clip = null; }
                _clip = AudioClip.Create("tts", ready.Samples.Length, 1, ready.Rate, false);
                _clip.SetData(ready.Samples, 0);
                _source.clip = _clip;
                _source.Play();
                _clipEndsAt = Time.time + (float)ready.Samples.Length / ready.Rate;
                Plugin.Log?.LogInfo($"PLAY {ready.Samples.Length} samples ({(float)ready.Samples.Length / ready.Rate:F2}s).");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Playback failed: " + ex.Message);
            }
        }

        // Direct preview (voice popup): bypass queue, play as soon as synth lands.
        public static void PlayPreview(float[] samples, int rate)
        {
            var ready = new ReadyClip { Item = new Item(), Samples = ApplyGain(Trim(samples), ModConfig.Volume.Value), Rate = rate };
            Main.Enqueue(() => Play(ready));
        }

        private static void EnsureAudioSource()
        {
            if (_source != null) return;

            var go = new GameObject("TtsExpanderAudio");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _source = go.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            Plugin.Log?.LogInfo("Created/recreated TTS AudioSource.");
        }

        private static readonly Regex IndexPrefix = new Regex(@"^\[\d+\]\s*", RegexOptions.Compiled);
        private static string StripIndex(string s) => IndexPrefix.Replace(s ?? "", "");

        private static string KeyOf(Item item) =>
            VoiceRegistry.KeyFor(item.SteamId, item.Pid) ?? ("~" + (item.Display ?? "?"));

        private static float[] Trim(float[] s)
        {
            const float thr = 400f / 32768f;
            int start = 0, end = s.Length;
            while (start < end && Math.Abs(s[start]) < thr) start++;
            while (end > start && Math.Abs(s[end - 1]) < thr) end--;
            int pad = 22050 * 15 / 1000;
            start = Math.Max(0, start - pad);
            end = Math.Min(s.Length, end + pad);
            if (end <= start) return s;
            var o = new float[end - start];
            Array.Copy(s, start, o, 0, o.Length);
            return o;
        }

        private static float[] ApplyGain(float[] s, float gain)
        {
            if (Math.Abs(gain - 1f) < 0.001f) return s;
            for (int i = 0; i < s.Length; i++)
            {
                float v = s[i] * gain;
                s[i] = v > 1f ? 1f : (v < -1f ? -1f : v);
            }
            return s;
        }
    }
}
