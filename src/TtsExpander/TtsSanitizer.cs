using System.Text.RegularExpressions;

namespace TtsExpander
{
    // Strips rich-text / color tags from chat so the speech engine
    // only hears the clean name + message. Visible chat is untouched.
    public static class TtsSanitizer
    {
        private static readonly Regex TagRegex = new Regex("<.*?>", RegexOptions.Compiled);
        private static readonly Regex SpaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        public static string StripRichText(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            string noTags = TagRegex.Replace(input, string.Empty);
            return SpaceRegex.Replace(noTags, " ").Trim();
        }
    }
}
