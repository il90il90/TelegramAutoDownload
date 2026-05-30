using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using TelegramClient;
using TelegramClient.Models;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    /// <summary>
    /// Tests for the FilterDialog pattern-validation and live-test logic.
    /// </summary>
    public class FilterDialogPatternValidationTests
    {
        [Theory]
        [InlineData(".*720p.*",           true)]
        [InlineData(@"(?i).*\.exe$",      true)]
        [InlineData(@"https?://\S+",      true)]
        [InlineData("(a|b)",              true)]
        [InlineData("",                   false)]
        [InlineData("   ",                false)]
        [InlineData("***invalid***",      false)]
        [InlineData("(unclosed",          false)]
        public void PatternEntry_IsValid_ReflectsRegexValidity(string pattern, bool expectedValid)
        {
            FilterPatternHelper.IsValidPattern(pattern).Should().Be(expectedValid);
        }

        [Theory]
        [InlineData(@"(?i).*720p.*",   "movie_720p_HDR.mkv",    true)]
        [InlineData(@"(?i).*720p.*",   "movie_1080p.mkv",       false)]
        [InlineData(@"(?i).*sample.*", "Big.Buck.Bunny.sample.avi", true)]
        [InlineData(@"(?i).*\.exe$",   "setup.exe",             true)]
        [InlineData(@"(?i).*\.exe$",   "setup.exe.txt",         false)]
        [InlineData(@"https?://\S+",   "https://example.com",   true)]
        [InlineData(@"https?://\S+",   "just some text",        false)]
        public void LiveTest_FileMode_ExcludeMatchesExpected(string pattern, string input, bool shouldMatch)
        {
            FilterPatternHelper.Matches(input, pattern).Should().Be(shouldMatch);
        }

        [Theory]
        [InlineData(@"(?i).*(subscribe|follow).*", "Please subscribe to my channel!", true)]
        [InlineData(@"(?i).*(subscribe|follow).*", "Today's weather is nice",         false)]
        public void LiveTest_MessageMode_IncludeMatchesExpected(string pattern, string input, bool shouldMatch)
        {
            FilterPatternHelper.Matches(input, pattern).Should().Be(shouldMatch);
        }

        [Fact]
        public void MultipleExcludePatterns_AnyMatchSkipsFile()
        {
            var patterns = new[]
            {
                new FilterPatternRule { Pattern = @"(?i).*720p.*", Mode = FilterPatternMode.Exclude },
                new FilterPatternRule { Pattern = @"(?i).*sample.*", Mode = FilterPatternMode.Exclude },
            };

            FilterPatternHelper.ShouldSkipFile("video_sample_hd.mp4", patterns).Should().BeTrue();
            FilterPatternHelper.ShouldSkipFile("documentary_4K.mp4", patterns).Should().BeFalse();
        }

        [Fact]
        public void RegexEscape_MakesLiteralPattern_ThatMatchesOriginalText()
        {
            var rawText = "Check out this: https://t.me/channel (special .chars!)";
            var escaped = Regex.Escape(rawText);
            Regex.IsMatch(rawText, escaped, RegexOptions.IgnoreCase).Should().BeTrue();
        }

        [Theory]
        [InlineData(@"(?i).*720p.*")]
        [InlineData(@"(?i).*1080p.*")]
        [InlineData(@"https?://\S+")]
        public void QuickPattern_IsValidRegex(string pattern)
        {
            FilterPatternHelper.IsValidPattern(pattern).Should().BeTrue();
        }

        [Fact]
        public void SaveLogic_ExcludesInvalidAndBlankPatterns()
        {
            var raw = new List<FilterPatternRule>
            {
                new() { Pattern = ".*720p.*", Mode = FilterPatternMode.Exclude },
                new() { Pattern = "   ", Mode = FilterPatternMode.Include },
                new() { Pattern = "***bad***", Mode = FilterPatternMode.Exclude },
                new() { Pattern = @"(?i).*\.zip$", Mode = FilterPatternMode.Include },
            };

            var saved = raw
                .Where(p => !string.IsNullOrWhiteSpace(p.Pattern) && FilterPatternHelper.IsValidPattern(p.Pattern))
                .ToList();

            saved.Should().HaveCount(2);
            saved.Should().Contain(p => p.Pattern == ".*720p.*" && p.Mode == FilterPatternMode.Exclude);
            saved.Should().Contain(p => p.Pattern == @"(?i).*\.zip$" && p.Mode == FilterPatternMode.Include);
        }
    }
}
