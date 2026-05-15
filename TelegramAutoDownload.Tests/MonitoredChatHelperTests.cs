using FluentAssertions;
using TelegramClient;
using TelegramClient.Models;
using TL;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class MonitoredChatHelperTests
    {
        [Fact]
        public void GetMonitoredChatIds_OnlyIncludesSelected()
        {
            var chats = new[]
            {
                new ChatDto { Id = 1, Name = "A", Selected = true },
                new ChatDto { Id = 2, Name = "B", Selected = false },
                new ChatDto { Id = 3, Name = "C", Selected = true },
            };

            MonitoredChatHelper.GetMonitoredChatIds(chats).Should().BeEquivalentTo([1L, 3L]);
        }

        [Fact]
        public void FindMonitored_RequiresSelectedAndMatchingPeer()
        {
            var chats = new[]
            {
                new ChatDto { Id = 100, Name = "Off", Selected = false },
                new ChatDto { Id = 200, Name = "On", Selected = true },
            };

            MonitoredChatHelper.FindMonitored(chats, 200)!.Name.Should().Be("On");
            MonitoredChatHelper.FindMonitored(chats, 100).Should().BeNull();
            MonitoredChatHelper.FindMonitored(chats, 999).Should().BeNull();
        }

        [Fact]
        public void PeerMatches_HandlesNegativeConfigId()
        {
            MonitoredChatHelper.PeerMatches(-200, 200).Should().BeTrue();
            MonitoredChatHelper.PeerMatches(200, 200).Should().BeTrue();
        }

        [Fact]
        public void GetPeerId_FromChannelPeer()
        {
            var msg = new Message
            {
                peer_id = new PeerChannel { channel_id = 1075658842 },
            };
            MonitoredChatHelper.GetPeerId(msg).Should().Be(1075658842);
        }
    }
}
