using System;
using System.IO;

namespace TelegramClient
{
    /// <summary>
    /// Resolves per-chat folder templates into concrete paths.
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
        /// Resolves a folder template string.
        /// Returns <c>null</c> when the template is null or whitespace (caller uses default layout).
        ///
        /// Two modes:
        /// • Relative template — contains tokens or plain segments. Result is relative and must be
        ///   combined with the base download path by the caller.
        ///   Example: "{ChatName}/{Year}-{Month}" → "MyChannel/2026-05"
        ///
        /// • Absolute path — the template starts with a drive letter or UNC root (Path.IsPathRooted).
        ///   Returned unchanged so the caller can detect it with Path.IsPathRooted and use it directly
        ///   without combining with the base download path.
        ///   Example: "C:\Downloads\MyChannel" → "C:\Downloads\MyChannel"
        /// </summary>
        /// <param name="template">Template string or absolute path</param>
        /// <param name="type">Value for {Type} token, e.g. "Videos"</param>
        /// <param name="chatName">Value for {ChatName} token</param>
        /// <param name="at">Override timestamp (defaults to DateTime.Now)</param>
        public static string? Resolve(string? template, string type, string chatName, DateTime? at = null)
        {
            if (string.IsNullOrWhiteSpace(template)) return null;

            // Absolute paths are returned as-is — caller should NOT combine with basePath.
            if (Path.IsPathRooted(template)) return template;

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
