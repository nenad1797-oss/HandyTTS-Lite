using HarmonyLib;
using NuclearOption.Networking;
using NuclearOption.UI;
using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TtsExpander
{
    // Adds "Assign TTS / Mute TTS / Rename TTS" to the leaderboard
    // right-click menu by cloning its own buttons. Popup is built from game-styled
    // parts (inherits the menu's font) so it looks native-ish.
    internal static class ScoreboardMenu
    {
        private static readonly System.Collections.Generic.HashSet<int> Decorated = new System.Collections.Generic.HashSet<int>();
        private static GameObject _popup;

        public static void OnMenuShown(LeaderBoardRightClickMenu __instance)
        {
            try
            {
                var menu = __instance;
                if (menu == null) return;
                ClosePopup();
                int id = menu.GetInstanceID();
                if (!Decorated.Contains(id))
                {
                    Decorate(menu);
                    Decorated.Add(id);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Scoreboard menu hook failed: " + ex.Message);
            }
        }

        private static void Decorate(LeaderBoardRightClickMenu menu)
        {
            var t = Traverse.Create(menu);
            Button template = t.Field("muteButton").GetValue<Button>();
            if (template == null) { Plugin.Log?.LogWarning("Scoreboard: muteButton not found."); return; }
            AddMenuButton(menu, template, "Assign TTS", () => OnVoice(menu));
            AddMenuButton(menu, template, "Mute TTS", () => OnMute(menu));
            AddMenuButton(menu, template, "Rename TTS", () => OnRename(menu));
            Plugin.Log?.LogInfo("Scoreboard menu decorated with TTS entries.");
        }

        private static void AddMenuButton(LeaderBoardRightClickMenu menu, Button template, string label, UnityAction onClick)
        {
            var go = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent);
            go.transform.SetAsLastSibling();
            go.name = "Tts_" + label.Replace(" ", "");
            var text = go.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null) text.text = label;
            var btn = go.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(onClick);
        }

        private struct Target
        {
            public bool Ok;
            public ulong SteamId;
            public uint Pid;
            public string Display;
        }

        private static System.Reflection.MethodInfo _tryGetPlayer;

        private static Target GetTarget(LeaderBoardRightClickMenu menu)
        {
            var t = new Target { Ok = false };
            try
            {
                if (_tryGetPlayer == null)
                    _tryGetPlayer = typeof(LeaderBoardRightClickMenu).GetMethod("TryGetPlayer",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (_tryGetPlayer == null) return t;
                object[] args = new object[] { null };
                bool ok = false;
                try { ok = (bool)_tryGetPlayer.Invoke(menu, args); } catch { return t; }
                Player player = args[0] as Player;
                if (!ok || player == null) return t;
                t.Ok = true;
                try { t.Display = player.GetDisplayName(PlayerNameContext.ChatOrLeaderboard); }
                catch { t.Display = "Someone"; }
                try { t.SteamId = player.SteamID; } catch { t.SteamId = 0; }
                try { t.Pid = player.PlayerRef.PlayerId; } catch { t.Pid = uint.MaxValue; }
            }
            catch (Exception ex) { Plugin.Log?.LogWarning("Scoreboard target resolve failed: " + ex.Message); }
            return t;
        }

        private static void OnMute(LeaderBoardRightClickMenu menu)
        {
            var t = GetTarget(menu);
            if (!t.Ok) return;
            VoiceRegistry.Resolve(t.SteamId, t.Pid, t.Display);
            string key = VoiceRegistry.KeyFor(t.SteamId, t.Pid);
            if (key == null) return;
            bool now = !VoiceRegistry.IsMuted(key);
            VoiceRegistry.SetMute(key, now);
            TtsDriver.SpeakLocal((now ? "Muted " : "Unmuted ") + t.Display);
        }

        private static void OnVoice(LeaderBoardRightClickMenu menu)
        {
            var t = GetTarget(menu);
            if (!t.Ok) return;
            OpenPopup(menu, "TTS voice for " + t.Display + " (0-903)",
                TMP_InputField.ContentType.IntegerNumber, "0 - 903",
                (input) =>
                {
                    int sid;
                    if (!int.TryParse(input.text, out sid) || sid < 0 || sid >= VoiceRegistry.NumSpeakers)
                    { TtsDriver.SpeakLocal("Enter a number 0 to 903."); return false; }
                    Task.Run(() =>
                    {
                        var s = TtsEngine.Synth("Hello, this is voice " + sid, sid, 1f, 1f, 0.5f, 1f);
                        if (s != null) TtsDriver.PlayPreview(s, TtsEngine.SampleRate);
                    });
                    return true; // keep open after preview
                },
                (input) =>
                {
                    int sid;
                    if (!int.TryParse(input.text, out sid) || sid < 0 || sid >= VoiceRegistry.NumSpeakers)
                    { TtsDriver.SpeakLocal("Enter a number 0 to 903."); return; }
                    VoiceRegistry.Resolve(t.SteamId, t.Pid, t.Display);
                    string key = VoiceRegistry.KeyFor(t.SteamId, t.Pid);
                    if (key == null) return;
                    VoiceRegistry.SetOverride(key, sid);
                    TtsDriver.SpeakLocal("Voice " + sid + " assigned to " + t.Display);
                    ClosePopup();
                });
        }

        private static void OnRename(LeaderBoardRightClickMenu menu)
        {
            var t = GetTarget(menu);
            if (!t.Ok) return;
            OpenPopup(menu, "TTS name for " + t.Display, TMP_InputField.ContentType.Standard, "spoken name",
                (input) =>
                {
                    string alias = (input.text ?? "").Trim();
                    if (alias.Length == 0) return false;
                    Task.Run(() =>
                    {
                        var s = TtsEngine.Synth(alias, 1, 1f, 1f, 0.5f, 1f);
                        if (s != null) TtsDriver.PlayPreview(s, TtsEngine.SampleRate);
                    });
                    return true; // keep open after preview
                },
                (input) =>
                {
                    string alias = (input.text ?? "").Trim();
                    if (alias.Length == 0) return;
                    VoiceRegistry.Resolve(t.SteamId, t.Pid, t.Display);
                    string key = VoiceRegistry.KeyFor(t.SteamId, t.Pid);
                    if (key == null) return;
                    VoiceRegistry.SetAlias(key, alias);
                    TtsDriver.SpeakLocal("Renamed to " + alias);
                    ClosePopup();
                });
        }

        private static void OpenPopup(LeaderBoardRightClickMenu menu, string title,
            TMP_InputField.ContentType contentType, string placeholder,
            Func<TMP_InputField, bool> onPreview, Action<TMP_InputField> onAssign)
        {
            ClosePopup();
            Transform parent = menu.transform;
            var canvas = menu.GetComponentInParent<Canvas>();
            if (canvas != null) parent = canvas.transform;

            var template = Traverse.Create(menu).Field("muteButton").GetValue<Button>();
            TextMeshProUGUI srcText = template != null ? template.GetComponentInChildren<TextMeshProUGUI>() : null;

            _popup = new GameObject("TtsPopup");
            _popup.transform.SetParent(parent, false);
            var root = _popup.AddComponent<RectTransform>();
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(440, 300);
            root.anchoredPosition = Vector2.zero;
            var bg = _popup.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.92f);

            TextMeshProUGUI label = AddText(_popup.transform, title, 22, 118);
            if (srcText != null) { label.font = srcText.font; }

            var inputObj = new GameObject("TtsInput");
            inputObj.transform.SetParent(_popup.transform, false);
            var inputRt = inputObj.AddComponent<RectTransform>();
            inputRt.anchorMin = inputRt.anchorMax = new Vector2(0.5f, 0.5f);
            inputRt.sizeDelta = new Vector2(380, 46);
            inputRt.anchoredPosition = new Vector2(0, 40);
            var inputBg = inputObj.AddComponent<Image>();
            inputBg.color = new Color(1f, 1f, 1f, 0.12f);

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(inputObj.transform, false);
            var textRt = textObj.AddComponent<RectTransform>();
            textRt.anchorMin = new Vector2(0, 0); textRt.anchorMax = new Vector2(1, 1);
            textRt.offsetMin = new Vector2(10, 6); textRt.offsetMax = new Vector2(-10, -6);
            var textMesh = textObj.AddComponent<TextMeshProUGUI>();
            textMesh.fontSize = 20;
            textMesh.color = Color.white;
            if (srcText != null) textMesh.font = srcText.font;

            var phObj = new GameObject("Placeholder");
            phObj.transform.SetParent(inputObj.transform, false);
            var phRt = phObj.AddComponent<RectTransform>();
            phRt.anchorMin = new Vector2(0, 0); phRt.anchorMax = new Vector2(1, 1);
            phRt.offsetMin = new Vector2(10, 6); phRt.offsetMax = new Vector2(-10, -6);
            var phMesh = phObj.AddComponent<TextMeshProUGUI>();
            phMesh.fontSize = 20;
            phMesh.color = new Color(1, 1, 1, 0.4f);
            phMesh.text = placeholder;
            if (srcText != null) phMesh.font = srcText.font;

            var input = inputObj.AddComponent<TMP_InputField>();
            input.textComponent = textMesh;
            input.placeholder = phMesh;
            input.contentType = contentType;
            if (contentType == TMP_InputField.ContentType.IntegerNumber) input.characterLimit = 3;
            else input.characterLimit = 24;

            float y = -60;
            if (onPreview != null)
            {
                AddPopupButton("Preview", new Vector2(-130, y), () => onPreview(input));
                AddPopupButton("Assign", new Vector2(0, y), () => onAssign(input));
                AddPopupButton("Close", new Vector2(130, y), () => ClosePopup());
            }
            else
            {
                AddPopupButton("Assign", new Vector2(-70, y), () => onAssign(input));
                AddPopupButton("Close", new Vector2(70, y), () => ClosePopup());
            }

            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(inputObj);
            input.ActivateInputField();
        }

        private static TextMeshProUGUI AddText(Transform parent, string s, int size, float y)
        {
            var go = new GameObject("TtsLabel");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(400, 36);
            rt.anchoredPosition = new Vector2(0, y);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = s;
            t.fontSize = size;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.Center;
            return t;
        }

        private static void AddPopupButton(string label, Vector2 pos, UnityAction onClick)
        {
            var go = new GameObject("TtsBtn_" + label);
            go.transform.SetParent(_popup.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(120, 44);
            rt.anchoredPosition = pos;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.16f, 0.42f, 0.16f, 1f);
            var btn = go.AddComponent<Button>();
            var txt = AddText(go.transform, label, 18, 0);
            var txtRt = txt.GetComponent<RectTransform>();
            txtRt.anchorMin = new Vector2(0, 0); txtRt.anchorMax = new Vector2(1, 1);
            txtRt.offsetMin = Vector2.zero; txtRt.offsetMax = Vector2.zero;
            btn.onClick.AddListener(onClick);
        }

        public static void ClosePopup()
        {
            if (_popup != null)
            {
                try { UnityEngine.Object.Destroy(_popup); } catch { }
                _popup = null;
            }
        }
    }
}
