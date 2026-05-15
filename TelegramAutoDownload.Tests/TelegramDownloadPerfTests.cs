using FluentAssertions;
using TelegramClient;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class TelegramDownloadPerfTests
    {
        [Theory]
        [InlineData(0, 1)]
        [InlineData(3, 3)]
        [InlineData(6, 6)]
        [InlineData(16, 16)]
        [InlineData(99, 16)]
        public void ClampDownloadThreads_RespectsBounds(int input, int expected)
        {
            TelegramDownloadPerf.ClampDownloadThreads(input).Should().Be(expected);
        }
    }
}
