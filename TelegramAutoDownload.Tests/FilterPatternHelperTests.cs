using FluentAssertions;
using TelegramClient;
using TelegramClient.Models;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class FilterPatternHelperTests
    {
        [Fact]
        public void ShouldSkipFile_ExcludeMatch_Skips()
        {
            var patterns = new[]
            {
                new FilterPatternRule { Pattern = @"(?i).*1080p.*", Mode = FilterPatternMode.Exclude },
            };

            FilterPatternHelper.ShouldSkipFile("movie_1080p.mkv", patterns).Should().BeTrue();
            FilterPatternHelper.ShouldSkipFile("movie_720p.mkv", patterns).Should().BeFalse();
        }

        [Fact]
        public void ShouldSkipFile_IncludeWhitelist_OnlyAllowsMatches()
        {
            var patterns = new[]
            {
                new FilterPatternRule { Pattern = @"(?i).*1080p.*", Mode = FilterPatternMode.Include },
            };

            FilterPatternHelper.ShouldSkipFile("movie_1080p.mkv", patterns).Should().BeFalse();
            FilterPatternHelper.ShouldSkipFile("movie_720p.mkv", patterns).Should().BeTrue();
        }

        [Fact]
        public void ShouldSkipFile_ExcludeWinsOverInclude()
        {
            var patterns = new[]
            {
                new FilterPatternRule { Pattern = @"(?i).*sample.*", Mode = FilterPatternMode.Exclude },
                new FilterPatternRule { Pattern = @"(?i).*1080p.*", Mode = FilterPatternMode.Include },
            };

            FilterPatternHelper.ShouldSkipFile("sample_1080p.mkv", patterns).Should().BeTrue();
        }

        [Fact]
        public void ShouldCaptureText_IncludeMatch_Captures()
        {
            var patterns = new[]
            {
                new FilterPatternRule { Pattern = @"(?i).*(subscribe|follow).*", Mode = FilterPatternMode.Include },
            };

            FilterPatternHelper.ShouldCaptureText("Please subscribe!", patterns).Should().BeTrue();
            FilterPatternHelper.ShouldCaptureText("plain weather update", patterns).Should().BeFalse();
        }

        [Fact]
        public void ShouldCaptureText_ExcludeBlocksCapture()
        {
            var patterns = new[]
            {
                new FilterPatternRule { Pattern = @"(?i).*1080p.*", Mode = FilterPatternMode.Exclude },
                new FilterPatternRule { Pattern = @"(?i).*1080p.*", Mode = FilterPatternMode.Include },
            };

            FilterPatternHelper.ShouldCaptureText("new 1080p release", patterns).Should().BeFalse();
        }

        [Fact]
        public void NormalizeChatFilters_MigratesLegacyIgnoreList()
        {
            var chat = new ChatDto
            {
                Name = "Test",
                IgnoreFileByRegex = ["(?i).*720p.*"],
            };

            FilterPatternHelper.NormalizeChatFilters(chat);

            chat.FilterPatterns.Should().ContainSingle();
            chat.FilterPatterns[0].Pattern.Should().Be("(?i).*720p.*");
            chat.FilterPatterns[0].Mode.Should().Be(FilterPatternMode.Exclude);
        }

        [Theory]
        [InlineData("movie_720p_HDR.mkv", true)]
        [InlineData("movie_1080p.mkv", false)]
        public void LiveTest_FileMode_ExcludeStillWorks(string input, bool shouldSkip)
        {
            var patterns = new[] { new FilterPatternRule { Pattern = @"(?i).*720p.*", Mode = FilterPatternMode.Exclude } };
            FilterPatternHelper.ShouldSkipFile(input, patterns).Should().Be(shouldSkip);
        }

        [Theory]
        [InlineData("Please subscribe to my channel!", true)]
        [InlineData("Today's weather is nice", false)]
        public void LiveTest_MessageMode_IncludeCapture(string input, bool shouldCapture)
        {
            var patterns = new[]
            {
                new FilterPatternRule { Pattern = @"(?i).*(subscribe|follow).*", Mode = FilterPatternMode.Include },
            };
            FilterPatternHelper.ShouldCaptureText(input, patterns).Should().Be(shouldCapture);
        }
    }
}
