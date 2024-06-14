using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TelegramAutoDownload.Models;
using TelegramClient.Factory.Service;
using TelegramClient.Models;
using TL;
using WTelegram;

namespace TelegramClient
{
    public class TelegramApp
    {
        public readonly Client Client;
        private FactoryMessagesService factoryService;
        private FactoryUserService factoryUserService;
        private ConfigParams _configParams;

        public TelegramApp(FactoryUserService factoryUserService)
        {
            this.factoryUserService = factoryUserService;
        }

        public TelegramApp(int appId, string apiHash)
        {
            Client = new Client(appId, apiHash, "session.dat");
            Client.LoginUserIfNeeded();
            Client.OnUpdates += Client_OnUpdates;
        }

        /// <summary>
        /// Update the configuration for chat IDs and folder for file saving.
        /// </summary>
        /// <param name="chatIds">The list of chat IDs to update.</param>
        /// <param name="pathFolderToSaveFiles">The path to the folder where files will be saved.</param>
        public void UpdateConfig(ConfigParams configParams)
        {
            _configParams = configParams;
            var chatIds = configParams.Chats.Select(c => c.Id).ToList();
            factoryService = new FactoryMessagesService(Client, configParams.PathSaveFile);
            factoryUserService = new FactoryUserService(chatIds);
        }

        private async Task Client_OnUpdates(UpdatesBase updates)
        {
            if (factoryUserService == null)
                return;

            var chat = factoryUserService.Execute(updates);
            if (chat == null) return;

            await factoryService.ExecuteAsync(updates.UpdateList, chat);

            if (_configParams.ReactionStatus)
            {
                var updateNewMessage = updates.UpdateList.OfType<UpdateNewMessage>().FirstOrDefault();
                if (updateNewMessage != null)
                {
                    var message = (Message)updateNewMessage.message;
                    await ReactToMessage(updates, message);
                }
            }
        }

        private async Task ReactToMessage(UpdatesBase updates, Message message)
        {
            var channel = ((Channel)((Updates)updates).Chats.First().Value);

            var inputPeer = new InputPeerChannel(channel.ID, channel.access_hash);
            await Client.Messages_SendReaction(inputPeer, message.ID, new[] { new ReactionEmoji { emoticon = _configParams.ReactionIcon } });
        }

        public async Task<IList<ChatDto>> GetAllChats()
        {
            var groups = new List<ChatDto>();
            var dialogs = await Client.Messages_GetAllDialogs();
            foreach (var dialog in dialogs.chats)
            {
                groups.Add(new ChatDto()
                {
                    Id = dialog.Value.ID,
                    Name = dialog.Value.Title,
                    Username = dialog.Value.MainUsername,
                    Type = "Group"
                });
            }
            foreach (var dialog in dialogs.users)
            {
                groups.Add(new ChatDto()
                {
                    Id = dialog.Value.ID,
                    Name = $"{dialog.Value.first_name} {dialog.Value.last_name}",
                    Username = dialog.Value.MainUsername,
                    Type = "User"
                });
            }
            return groups;
        }
    }
}
