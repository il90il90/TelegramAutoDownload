using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TelegramClient.Models;

namespace TelegramClient
{
    public static class FilterPatternHelper
    {
        public static bool IsValidPattern(string? pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return false;
            try
            {
                _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool Matches(string input, string pattern)
        {
            if (string.IsNullOrEmpty(input) || !IsValidPattern(pattern)) return false;
            try
            {
                return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Ensures <see cref="ChatDto.FilterPatterns"/> is populated from legacy <see cref="ChatDto.IgnoreFileByRegex"/>.
        /// </summary>
        public static void NormalizeChatFilters(ChatDto chat)
        {
            chat.FilterPatterns ??= [];

            if (chat.FilterPatterns.Count == 0 && chat.IgnoreFileByRegex is { Count: > 0 })
            {
                foreach (var pattern in chat.IgnoreFileByRegex)
                {
                    if (string.IsNullOrWhiteSpace(pattern)) continue;
                    chat.FilterPatterns.Add(new FilterPatternRule
                    {
                        Pattern = pattern,
                        Mode = FilterPatternMode.Exclude,
                    });
                }
            }

            SyncLegacyExcludeList(chat);
        }

        /// <summary>Keeps legacy exclude-only list in sync for older configs/tools.</summary>
        public static void SyncLegacyExcludeList(ChatDto chat)
        {
            chat.IgnoreFileByRegex = chat.FilterPatterns
                .Where(p => p.Mode == FilterPatternMode.Exclude && !string.IsNullOrWhiteSpace(p.Pattern))
                .Select(p => p.Pattern)
                .ToList();
        }

        public static IReadOnlyList<FilterPatternRule> GetPatterns(ChatDto chat)
        {
            NormalizeChatFilters(chat);
            return chat.FilterPatterns;
        }

        public static bool HasPatterns(ChatDto chat)
        {
            NormalizeChatFilters(chat);
            return chat.FilterPatterns.Any(p => !string.IsNullOrWhiteSpace(p.Pattern));
        }

        /// <summary>True when the file should NOT be downloaded.</summary>
        public static bool ShouldSkipFile(string fileName, IReadOnlyList<FilterPatternRule> patterns)
        {
            if (string.IsNullOrEmpty(fileName) || patterns.Count == 0) return false;

            var excludes = patterns.Where(p => p.Mode == FilterPatternMode.Exclude).ToList();
            var includes = patterns.Where(p => p.Mode == FilterPatternMode.Include).ToList();

            if (excludes.Any(p => Matches(fileName, p.Pattern)))
                return true;

            if (includes.Count > 0 && !includes.Any(p => Matches(fileName, p.Pattern)))
                return true;

            return false;
        }

        /// <summary>True when a text-only message should be saved as a .txt capture file.</summary>
        public static bool ShouldCaptureText(string text, IReadOnlyList<FilterPatternRule> patterns)
        {
            if (string.IsNullOrWhiteSpace(text) || patterns.Count == 0) return false;

            if (patterns.Any(p => p.Mode == FilterPatternMode.Exclude && Matches(text, p.Pattern)))
                return false;

            return patterns.Any(p => p.Mode == FilterPatternMode.Include && Matches(text, p.Pattern));
        }

        public static string DescribeFileAction(FilterPatternRule rule, bool matched)
        {
            if (rule.Mode == FilterPatternMode.Exclude)
                return matched ? "→ skip file" : "→ no block";

            return matched ? "→ allowed (include match)" : "→ blocked (include whitelist)";
        }

        public static string DescribeMessageAction(FilterPatternRule rule, bool matched)
        {
            if (rule.Mode == FilterPatternMode.Include)
                return matched ? "→ capture as .txt" : "→ no capture";

            return matched ? "→ skip text" : "→ no action";
        }

        public static string DescribeOverallFileOutcome(string input, IReadOnlyList<FilterPatternRule> validPatterns)
        {
            if (validPatterns.Count == 0) return string.Empty;
            var skip = ShouldSkipFile(input, validPatterns);
            return skip
                ? "Overall: file would be SKIPPED"
                : "Overall: file would be DOWNLOADED";
        }

        public static string DescribeOverallMessageOutcome(string input, IReadOnlyList<FilterPatternRule> validPatterns)
        {
            if (validPatterns.Count == 0) return string.Empty;
            var capture = ShouldCaptureText(input, validPatterns);
            return capture
                ? "Overall: message would be CAPTURED as .txt"
                : "Overall: message would NOT be captured";
        }
    }
}
