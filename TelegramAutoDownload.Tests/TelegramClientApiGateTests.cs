using System.Threading.Tasks;
using FluentAssertions;
using TelegramClient;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class TelegramClientApiGateTests
    {
        [Fact]
        public async Task Nested_RunAsync_does_not_deadlock()
        {
            var outerRan = false;
            var innerRan = false;

            await TelegramClientApiGate.RunAsync(async () =>
            {
                outerRan = true;
                await TelegramClientApiGate.RunAsync(() => { innerRan = true; return Task.CompletedTask; });
            });

            outerRan.Should().BeTrue();
            innerRan.Should().BeTrue();
        }
    }
}
