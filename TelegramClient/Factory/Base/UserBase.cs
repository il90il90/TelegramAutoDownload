using System.Collections.Generic;
using System.Linq;
using TelegramClient.Factory.FactoriesMessages;
using TelegramClient.Factory.Interfaces.Channel;
using TelegramClient.Models;
using TL;

namespace TelegramClient.Factory.Base
{
    public abstract class UserBase : IChannel
    {
        public abstract ChatDto Execute(UpdatesBase updates);

        public readonly IList<long> listenToChannel;

        public UserBase(IList<long> listenToChannel)
        {
            this.listenToChannel = listenToChannel;
        }
    }
}
