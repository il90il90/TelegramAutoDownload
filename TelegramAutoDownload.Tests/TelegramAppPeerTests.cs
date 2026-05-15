using FluentAssertions;
using TelegramClient;
using TelegramClient.Models;
using TL;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class TelegramAppPeerTests
    {
        [Fact]
        public void BuildInputPeer_User_uses_InputPeerUser()
        {
            var chat = new ChatDto { Id = 100, Type = "User", Name = "u" };
            var peer = TelegramApp.BuildInputPeer(chat, 999);
            peer.Should().BeOfType<InputPeerUser>();
            ((InputPeerUser)peer).user_id.Should().Be(100);
            ((InputPeerUser)peer).access_hash.Should().Be(999);
        }

        [Fact]
        public void BuildInputPeer_Channel_uses_InputPeerChannel()
        {
            var chat = new ChatDto { Id = 200, Type = "Channel", Name = "ch" };
            var peer = TelegramApp.BuildInputPeer(chat, 12345);
            peer.Should().BeOfType<InputPeerChannel>();
        }

        [Fact]
        public void BuildInputPeer_BasicGroup_uses_InputPeerChat()
        {
            var chat = new ChatDto { Id = 300, Type = "Group", Name = "g" };
            var peer = TelegramApp.BuildInputPeer(chat, 0);
            peer.Should().BeOfType<InputPeerChat>();
        }

        [Fact]
        public void BuildInputPeer_Supergroup_uses_InputPeerChannel()
        {
            var chat = new ChatDto { Id = 400, Type = "Group", Name = "sg" };
            var peer = TelegramApp.BuildInputPeer(chat, 555);
            peer.Should().BeOfType<InputPeerChannel>();
        }
    }
}
