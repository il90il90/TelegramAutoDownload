using System.Collections.Generic;
using System.Threading.Channels;
using TelegramClient.Factory.FactoriesUsers;
using TelegramClient.Factory.Interfaces.Channel;
using TelegramClient.Models;
using TL;

namespace TelegramClient.Factory.Service
{
    public class FactoryUserService
    {
        private readonly IList<IChannel> _channels;
        private readonly IList<long> _listenToChannelId;

        public FactoryUserService(IList<long> listenToChannel)
        {
            _listenToChannelId = listenToChannel;
            _channels = new List<IChannel>()
            {
                new Group(_listenToChannelId),
                new FactoriesUsers.User(_listenToChannelId),
            };
        }

        public ChatDto Execute(UpdatesBase updates)
        {
            foreach (var channels in _channels)
            {
                var found = channels.Execute(updates);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
