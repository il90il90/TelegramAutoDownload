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

                if (updates is not Updates typedUpdates) return null;

                // Enumerate without Cast — Dictionary<long, UserBase> yields KeyValuePair<long, UserBase>
                var userKvp = typedUpdates.Users?
                    .FirstOrDefault(u => listenToChannel.Contains(u.Key));
                if (userKvp == null || userKvp.Value.Value is not TL.User tlUser) return null;
                var username = tlUser.MainUsername?.Replace("@", "");
                if (username == null) return null;
                var chatParams = ConfigParams.Chats.FirstOrDefault(a => a.Id == userKvp.Value.Key);
                if (chatParams == null) return null;

                return new ChatDto()
                {
                    Id = tlUser.ID,
                    Name = $"{tlUser.first_name} {tlUser.last_name}",
                    Username = username,
                    ReactionIcon = chatParams.ReactionIcon,
                    DownloadStartIcon = chatParams.DownloadStartIcon,
                    Download = chatParams.Download,
                    Type = chatParams.Type,
                    DownloadFromSize = chatParams.DownloadFromSize,
                    IgnoreFileByRegex = chatParams.IgnoreFileByRegex,
                    Selected = chatParams.Selected,
                    EnabledPlugins = chatParams.EnabledPlugins ?? new Dictionary<string, bool>(),
                };
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
