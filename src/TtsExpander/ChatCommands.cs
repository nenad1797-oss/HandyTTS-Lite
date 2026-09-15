using System;

namespace TtsExpander
{
    // Phase-1 control surface: /t chat commands (swallowed, never sent to server).
    internal static class ChatCommands
    {
        public static bool TryHandle(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            string m = message.Trim();
            if (!m.StartsWith("/tvoice", StringComparison.OrdinalIgnoreCase)
                && !m.StartsWith("/tmute", StringComparison.OrdinalIgnoreCase)
                && !m.StartsWith("/tunmute", StringComparison.OrdinalIgnoreCase)
                && !m.StartsWith("/tname", StringComparison.OrdinalIgnoreCase)
                && !m.StartsWith("/thelp", StringComparison.OrdinalIgnoreCase))
                return false;
            try
            {
                var parts = m.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string cmd = parts[0].ToLowerInvariant();
                if (cmd == "/thelp")
                {
                    TtsDriver.SpeakLocal("Commands: t voice, name, sid. t mute, name. t unmute, name. t name, name, alias.");
                    Plugin.Log?.LogInfo("T Commands: /tvoice <name> <sid 0-903> | /tmute <name> | /tunmute <name> | /tname <name> <alias>");
                    return true;
                }
                if (cmd == "/tvoice" && parts.Length >= 3)
                {
                    string name = string.Join(" ", parts, 1, parts.Length - 2);
                    if (int.TryParse(parts[parts.Length - 1], out int sid) && sid >= 0 && sid < VoiceRegistry.NumSpeakers)
                    {
                        string key = VoiceRegistry.FindByName(name);
                        if (key == "") { Say("No known player matching " + name); return true; }
                        VoiceRegistry.SetOverride(key, sid);
                        Say("Voice " + sid + " assigned to " + VoiceRegistry.Describe(key));
                    }
                    else Say("Usage: slash t voice, name, sid 0 to 903");
                    return true;
                }
                if ((cmd == "/tmute" || cmd == "/tunmute") && parts.Length >= 2)
                {
                    string name = string.Join(" ", parts, 1, parts.Length - 1);
                    string key = VoiceRegistry.FindByName(name);
                    if (key == "") { Say("No known player matching " + name); return true; }
                    VoiceRegistry.SetMute(key, cmd == "/tmute");
                    Say((cmd == "/tmute" ? "Muted " : "Unmuted ") + VoiceRegistry.Describe(key));
                    return true;
                }
                if (cmd == "/tname" && parts.Length >= 3)
                {
                    string alias = parts[parts.Length - 1];
                    string name = string.Join(" ", parts, 1, parts.Length - 2);
                    string key = VoiceRegistry.FindByName(name);
                    if (key == "") { Say("No known player matching " + name); return true; }
                    VoiceRegistry.SetAlias(key, alias);
                    Say("Rename set: " + VoiceRegistry.Describe(key));
                    return true;
                }
                Say("Unknown T command. Try slash t help.");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Command failed: " + ex.Message);
                return true;
            }
        }

        private static void Say(string text)
        {
            Plugin.Log?.LogInfo("[TTS] " + text);
            TtsDriver.SpeakLocal(text);
        }
    }
}
