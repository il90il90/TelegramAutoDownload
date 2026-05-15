using System.Collections.Generic;
using System.Linq;
using TelegramClient.Models;
using TL;

namespace TelegramClient
{
    /// <summary>
    /// Resolves which configured chats are actively monitored (UI checkbox = Selected).
    /// </summary>
    public static class MonitoredChatHelper
    {
        public static long GetPeerId(Message message) =>
            message.peer_id switch
            {
                PeerChannel pc => pc.channel_id,
                PeerChat pg => pg.chat_id,
                PeerUser pu => pu.user_id,
                _ => 0
            };

        public static bool PeerMatches(long configChatId, long peerId) =>
            peerId != 0 && (configChatId == peerId || configChatId == -peerId);

        /// <summary>Returns the configured chat row when it is selected for monitoring and the peer ID matches.</summary>
        public static ChatDto? FindMonitored(IEnumerable<ChatDto>? chats, long peerId) =>
            chats?.FirstOrDefault(c => c.Selected && PeerMatches(c.Id, peerId));

        public static IList<long> GetMonitoredChatIds(IEnumerable<ChatDto>? chats) =>
            chats?.Where(c => c.Selected).Select(c => c.Id).ToList() ?? [];
    }
}
