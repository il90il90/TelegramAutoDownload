using FluentAssertions;
using TelegramClient;
using TL;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class TelegramSessionHelperTests
    {
        [Fact]
        public void IsAuthKeyError_detects_RpcException_404()
        {
            var ex = new RpcException(404, "AUTH_KEY_NOT_FOUND");
            TelegramSessionHelper.IsAuthKeyError(ex).Should().BeTrue();
        }

        [Fact]
        public void IsAuthKeyError_false_for_other_errors()
        {
            TelegramSessionHelper.IsAuthKeyError(new InvalidOperationException("network")).Should().BeFalse();
        }
    }
}
