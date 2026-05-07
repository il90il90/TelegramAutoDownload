using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    /// <summary>
    /// Tests for the FilterDialog pattern-validation and live-test logic.
    /// The dialog is WPF-only; these tests cover the pure-logic layer used inside it.
    /// </summary>
    public class FilterDialogPatternValidationTests
    {
        // ── IsValid ──────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(".*720p.*",           true)]
        [InlineData(@"(?i).*\.exe$",      true)]
        [InlineData(@"https?://\S+",      true)]
        [InlineData("(a|b)",              true)]
        [InlineData("",                   false)]
        [InlineData("   ",                false)]
        [InlineData("***invalid***",      false)]   // repeated quantifiers
        [InlineData("(unclosed",          false)]   // unmatched parenthesis
        public void PatternEntry_IsValid_ReflectsRegexValidity(string pattern, bool expectedValid)
        {
            var isValid = IsPatternValid(pattern);
            isValid.Should().Be(expectedValid);
        }

        // ── Live test — file mode ────────────────────────────────────────────────

        [Theory]
        [InlineData(@"(?i).*720p.*",   "movie_720p_HDR.mkv",    true)]
        [InlineData(@"(?i).*720p.*",   "movie_1080p.mkv",       false)]
        [InlineData(@"(?i).*sample.*", "Big.Buck.Bunny.sample.avi", true)]
        [InlineData(@"(?i).*\.exe$",   "setup.exe",             true)]
        [InlineData(@"(?i).*\.exe$",   "setup.exe.txt",         false)]
        [InlineData(@"https?://\S+",   "https://example.com",   true)]
        [InlineData(@"https?://\S+",   "just some text",        false)]
        public void LiveTest_FileMode_MatchesExpected(string pattern, string input, bool shouldMatch)
        {
            var matched = TestMatch(pattern, input);
            matched.Should().Be(shouldMatch,
                because: $"pattern '{pattern}' on input '{input}'");
        }

        // ── Live test — message mode (text capture) ──────────────────────────────

        [Theory]
        [InlineData(@"(?i).*(subscribe|follow).*", "Please subscribe to my channel!", true)]
        [InlineData(@"(?i).*(subscribe|follow).*", "Today's weather is nice",         false)]
        [InlineData(@"(?i).*(promo|sponsor).*",    "This video is sponsored by X",    true)]
        [InlineData(@"https?://\S+",               "Download at https://example.com", true)]
        [InlineData(@"https?://\S+",               "No links here",                   false)]
        public void LiveTest_MessageMode_MatchesExpected(string pattern, string input, bool shouldMatch)
        {
            var matched = TestMatch(pattern, input);
            matched.Should().Be(shouldMatch);
        }

        // ── Multiple patterns — OR behaviour ────────────────────────────────────

        [Fact]
        public void MultiplePatterns_AnyMatchTriggersResult()
        {
            var patterns = new[] { @"(?i).*720p.*", @"(?i).*sample.*" };
            var input    = "video_sample_hd.mp4";

            var anyMatch = patterns.Any(p => TestMatch(p, input));
            anyMatch.Should().BeTrue(because: "'sample' pattern should match");
        }

        [Fact]
        public void MultiplePatterns_NoneMatchReturnsFalse()
        {
            var patterns = new[] { @"(?i).*720p.*", @"(?i).*1080p.*" };
            var input    = "documentary_4K.mp4";

            var anyMatch = patterns.Any(p => TestMatch(p, input));
            anyMatch.Should().BeFalse();
        }

        // ── Regex-escape for history text ────────────────────────────────────────

        [Fact]
        public void RegexEscape_MakesLiteralPattern_ThatMatchesOriginalText()
        {
            var rawText = "Check out this: https://t.me/channel (special .chars!)";
            var escaped = Regex.Escape(rawText);

            // The escaped pattern should match the original text
            Regex.IsMatch(rawText, escaped, RegexOptions.IgnoreCase).Should().BeTrue();
        }

        [Fact]
        public void RegexEscape_DoesNotMatchUnrelatedText()
        {
            var rawText = "subscribe now!";
            var escaped = Regex.Escape(rawText);

            Regex.IsMatch("different text", escaped, RegexOptions.IgnoreCase).Should().BeFalse();
        }

        // ── Quick patterns are valid regex ───────────────────────────────────────

        [Theory]
        [InlineData(@"(?i).*720p.*")]
        [InlineData(@"(?i).*1080p.*")]
        [InlineData(@"(?i).*(4k|2160p).*")]
        [InlineData(@"(?i).*hdr.*")]
        [InlineData(@"(?i).*sample.*")]
        [InlineData(@"(?i).*(promo|advertisement|\bad\b|sponsor)")]
        [InlineData(@"(?i).*\.exe$")]
        [InlineData(@"(?i).*\.zip$")]
        [InlineData(@"(?i).*\.(rar|7z)$")]
        [InlineData(@"https?://\S+")]
        [InlineData(@"(?i).*(subscribe|follow|like).*")]
        [InlineData(@"(?i).*(join|invite|channel).*")]
        public void QuickPattern_IsValidRegex(string pattern)
        {
            IsPatternValid(pattern).Should().BeTrue(
                because: $"quick pattern '{pattern}' must be a valid regex");
        }

        // ── Result pattern list excludes blank / invalid entries ─────────────────

        [Fact]
        public void SaveLogic_ExcludesInvalidAndBlankPatterns()
        {
            var raw = new[] { ".*720p.*", "   ", "***bad***", @"(?i).*\.zip$" };

            var saved = raw
                .Where(p => !string.IsNullOrWhiteSpace(p) && IsPatternValid(p))
                .ToList();

            saved.Should().HaveCount(2);
            saved.Should().Contain(".*720p.*");
            saved.Should().Contain(@"(?i).*\.zip$");
            saved.Should().NotContain("***bad***");
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static bool IsPatternValid(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return false;
            try { _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1)); return true; }
            catch { return false; }
        }

        private static bool TestMatch(string pattern, string input)
        {
            try { return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)); }
            catch { return false; }
        }
    }
}
