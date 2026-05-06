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
                var chatParams = ConfigParams.Chats.FirstOrDefault(a => a.Id == group.ID);
                if (chatParams == null) return null;

                return new ChatDto()
                {
                    Id = group.ID,
                    Name = group.Title,
                    Username = group.MainUsername,
                    ReactionIcon = chatParams.ReactionIcon,
                    DownloadStartIcon = chatParams.DownloadStartIcon,
                    Download = chatParams.Download,
                    Type = chatParams.Type,
                    DownloadFromSize = chatParams.DownloadFromSize,
                    IgnoreFileByRegex = chatParams.IgnoreFileByRegex,
                    Selected = chatParams.Selected,
                    EnabledPlugins = chatParams.EnabledPlugins ?? new Dictionary<string, bool>(),
                    YtdlpQuality = chatParams.YtdlpQuality,
                    FolderTemplate = chatParams.FolderTemplate,
                    SaveHistory = chatParams.SaveHistory,
                };
            }
            return null;
        }
    }
}
