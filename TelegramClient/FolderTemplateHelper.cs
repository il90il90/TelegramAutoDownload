using System;
using System.IO;

namespace TelegramClient
{
    /// <summary>
    /// Resolves per-chat folder templates into concrete relative paths.
    /// Extracted from BaseMessage so the resolution logic can be unit-tested independently.
    /// </summary>
    public static class FolderTemplateHelper
    {
        /// <summary>
        /// Supported tokens: {Type}, {ChatName}, {Year}, {Month}, {Day}.
        /// </summary>
        public static readonly string[] SupportedTokens =
            ["{Type}", "{ChatName}", "{Year}", "{Month}", "{Day}"];

        /// <summary>
        /// Resolves a folder template string into a relative path.
        /// Returns <c>null</c> when the template is null or whitespace (use default layout).
        /// Invalid path characters inside resolved tokens are replaced with underscores.
        /// </summary>
        /// <param name="template">Template string, e.g. "{ChatName}/{Year}-{Month}"</param>
        /// <param name="type">Value for {Type} token, e.g. "Videos"</param>
        /// <param name="chatName">Value for {ChatName} token</param>
        /// <param name="at">Override timestamp (defaults to DateTime.Now)</param>
        public static string? Resolve(string? template, string type, string chatName, DateTime? at = null)
        {
            if (string.IsNullOrWhiteSpace(template)) return null;

            var now  = at ?? DateTime.Now;
            var safe = SanitizeName(chatName);

            var resolved = template
                .Replace("{Type}",     type,                StringComparison.OrdinalIgnoreCase)
                .Replace("{ChatName}", safe,                 StringComparison.OrdinalIgnoreCase)
                .Replace("{Year}",     now.ToString("yyyy"), StringComparison.OrdinalIgnoreCase)
                .Replace("{Month}",    now.ToString("MM"),   StringComparison.OrdinalIgnoreCase)
                .Replace("{Day}",      now.ToString("dd"),   StringComparison.OrdinalIgnoreCase);

            // Sanitize any remaining invalid path chars that came from the template itself
            foreach (char c in Path.GetInvalidPathChars())
                resolved = resolved.Replace(c, '_');

            return resolved;
        }

        private static string SanitizeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, ' ');
            return name.TrimEnd();
        }
    }
}
