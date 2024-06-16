using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TelegramAutoDownload.Models;
using TelegramClient.Models;
using TL;

namespace TelegramClient.Factory.FactoriesUsers
{
    internal class Group : Base.UserBase
    {
        public Group(IList<long> listenToChannel, ConfigParams configParams) : base(listenToChannel, configParams)
        {
        }

        public override ChatDto Execute(UpdatesBase updates)
        {
            var group = updates.Chats.Values.FirstOrDefault(c => listenToChannel.Contains(c.ID));

            if (group != null)
            {
                var chat = updates.Chats?.Values?.FirstOrDefault();
                var chatParams = ConfigParams.Chats.FirstOrDefault(a => a.Id == chat.ID);
                return new ChatDto()
                {
                    Id = chat.ID,
                    Name = chat.Title,
                    Username = chat.MainUsername,
                    ReactionIcon = chatParams.ReactionIcon,
                    Download = chatParams.Download,
                };
            }
            return null;
        }
    }
}
