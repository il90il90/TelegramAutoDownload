using System;
using System.Collections.Generic;
using System.Text;
using TelegramClient.Models;
using TL;

namespace TelegramClient.Factory.Interfaces.Channel
{
    public interface IChannel
    {
        public ChatDto Execute(UpdatesBase updates);
    }
}
