using System;
using System.Collections.Generic;
using System.Linq;
using TelegramAutoDownload.Models;
using TelegramClient.Models;
using TL;

namespace TelegramClient.Factory.FactoriesUsers
{

    public class User : Base.UserBase
    {
        public User(IList<long> listenToChannel, ConfigParams configParams) : base(listenToChannel, configParams)
        {
        }

        public override ChatDto Execute(UpdatesBase updates)
        {
            try
            {
                if (updates.Users.Count == 0)
                    return null;

                var user = ((Updates)updates).Users?.FirstOrDefault(u => listenToChannel.Contains(u.Key));
                var username = user.Value.Value?.ToString().Replace("@", "");
                if (username == null) return null;
                var chatParams = ConfigParams.Chats.FirstOrDefault(a => a.Id == user.Value.Key);

                return new ChatDto()
                {
                    Id = user.Value.Value.ID,
                    Name = $"{user.Value.Value.first_name} {user.Value.Value.last_name}",
                    Username = username,
                    ReactionIcon = chatParams.ReactionIcon,
                    Download = chatParams.Download,
                    Type = chatParams.Type,
                    DownloadFromSize = chatParams.DownloadFromSize,
                    IgnoreFileByRegex = chatParams.IgnoreFileByRegex,
                    Selected = chatParams.Selected,
                };
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
