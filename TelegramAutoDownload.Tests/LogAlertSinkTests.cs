using FluentAssertions;
using TelegramAutoDownload.Services;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class LogAlertSinkTests
    {
        [Fact]
        public void BuildSearchAnchor_UsesMessageBodyWhenLongEnough()
        {
            var msg = "Download failed chat=Test error=timeout";
            var anchor = LogAlertSink.BuildSearchAnchor("2025-01-01 [ERR] " + msg, msg);
            anchor.Should().Be(msg);
        }

        [Fact]
        public void GetCurrentLogFilePath_UsesDailyFileName()
        {
            var at = new DateTimeOffset(2025, 5, 15, 12, 0, 0, TimeSpan.Zero);
            var path = LogAlertSink.GetCurrentLogFilePath(at);
            path.Should().EndWith("app-20250515.log");
        }
    }
}
