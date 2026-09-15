using BepInEx;
using SherpaOnnx;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace TtsExpander
{
    // Owns the Sherpa-ONNX/Piper engine. All synth goes through here, under one lock.
    internal static class TtsEngine
    {
        private static readonly object Gate = new object();
        private static OfflineTts _tts;
        private static OfflineTtsConfig _config;
        private static bool _failed;
        private static readonly OfflineTtsCallbackProgressWithArg NoopCb =
            new OfflineTtsCallbackProgressWithArg((IntPtr s, int n, float p, IntPtr a) => 1);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        public static int SampleRate => _tts?.SampleRate ?? 22050;

        public static bool EnsureReady()
        {
            if (_tts != null) return true;
            if (_failed) return false;
            lock (Gate)
            {
                if (_tts != null) return true;
                try
                {
                    string pluginDir = Path.GetDirectoryName(typeof(TtsEngine).Assembly.Location);
                    foreach (var dll in new[] { "onnxruntime.dll", "sherpa-onnx-c-api.dll" })
                    {
                        string full = Path.Combine(pluginDir, dll);
                        if (File.Exists(full) && LoadLibrary(full) == IntPtr.Zero)
                            Plugin.Log?.LogWarning("LoadLibrary failed for " + dll);
                    }
                    string modelDir = ModConfig.ModelDir.Value;
                    if (string.IsNullOrEmpty(modelDir))
                        modelDir = Path.Combine(pluginDir, "data");
                    string model = Path.Combine(modelDir, "en_US-libritts_r-medium.onnx");
                    string tokens = Path.Combine(modelDir, "tokens.txt");
                    string dataDir = Path.Combine(modelDir, "espeak-ng-data");
                    if (!File.Exists(model) || !File.Exists(tokens))
                    {
                        Plugin.Log?.LogError("Piper model not found in " + modelDir + " (set ModelDir in F1).");
                        _failed = true;
                        return false;
                    }
                    _config = new OfflineTtsConfig();
                    _config.Model.Vits.Model = model;
                    _config.Model.Vits.Tokens = tokens;
                    _config.Model.Vits.DataDir = dataDir;
                    _config.Model.Vits.LengthScale = 1.0f;
                    _config.Model.NumThreads = 2;
                    _config.Model.Debug = 0;
                    _config.Model.Provider = "cpu";
                    _config.MaxNumSentences = 1;
                    _tts = new OfflineTts(_config);
                    Plugin.Log?.LogInfo("Piper engine ready (" + model + ").");
                    return true;
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogError("Piper engine failed to start: " + ex.Message);
                    _failed = true;
                    return false;
                }
            }
        }

        public static float[] Synth(string text, int sid, float speed, float silence, float noise, float noiseW)
        {
            lock (Gate)
            {
                if (!EnsureReady()) return null;
                _config.Model.Vits.NoiseScale = noise;
                _config.Model.Vits.NoiseScaleW = noiseW;
                var gen = new OfflineTtsGenerationConfig { Sid = sid, Speed = speed, SilenceScale = silence };
                return _tts.GenerateWithConfig(text, gen, NoopCb).Samples;
            }
        }
    }
}
