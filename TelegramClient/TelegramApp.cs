using Serilog;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TelegramAutoDownload.Models;
using TelegramClient.Factory.FactoriesMessages.Enum;
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
        private readonly Serilog.Core.Logger logger;

        public TelegramApp(int appId, string apiHash, Logger.Logger logger = null)
        {
            if (logger != null)
                this.logger = logger.GetInstance();
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
            var chatIds = configParams.Chats.Select(c => c.Id).ToList();
            factoryService = new FactoryMessagesService(Client, configParams.PathSaveFile);
            factoryUserService = new FactoryUserService(chatIds, configParams);
        }

        private async Task Client_OnUpdates(UpdatesBase updates)
        {
            if (factoryUserService == null)
                return;

            var chat = factoryUserService.Execute(updates);
            if (chat == null) return;
            List<Task> tasks = [];
            foreach (Update update in updates.UpdateList)
            {
                if (update is UpdateNewMessage updateNewMessage)
                {
                    try
                    {
                        var task = Task.Run(async () =>
                        {
                            var resultExecute = await factoryService.ExecuteAsync(updateNewMessage, chat);
                            var infoMessage = (Message)updateNewMessage.message;

                            logger?.Information($"message from {chat.Name}: {infoMessage.message}. {{@fromUser}}{{@message}}{{@id}}{{@username}}{{@chatName}}{{@type}}{{@download}}{{@reactionIcon}}{{@resultExecute}}",
                                infoMessage.post_author, infoMessage.message, chat.Id, chat.Username ?? "private", chat.Name, chat.Type, chat.Download, chat.ReactionIcon, resultExecute);

                            if (resultExecute && chat.ReactionIcon != null)
                            {
                                if (updateNewMessage != null)
                                {
                                    await ReactToMessage(updates, infoMessage, chat.ReactionIcon);
                                }
                            }
                        });
                        tasks.Add(task);
                    }
                    catch (Exception ex)
                    {
                        logger?.Error($"error on :{chat.Name} {{errorMessage}}", ex.Message);
                    }
                }
            }
            await Task.WhenAll(tasks);
        }

        private async Task ReactToMessage(UpdatesBase updates, Message message, string reactionIcon)
        {
            InputPeer inputPeer;
            var isCahnnel = updates?.Chats?.FirstOrDefault().Value?.IsChannel;
            if (isCahnnel == true)
            {
                var channel = ((Channel)((Updates)updates).Chats.First().Value);
                inputPeer = new InputPeerChannel(channel.ID, channel.access_hash);
            }
            else
            {
                var user = updates.Users.FirstOrDefault().Value;
                inputPeer = new InputUser(user.id, user.access_hash);
            }
            await Client.Messages_SendReaction(inputPeer, message.ID, new[] { new ReactionEmoji { emoticon = reactionIcon } });
        }

        public async Task<IList<ChatDto>> GetAllChats()
        {
            var groups = new List<ChatDto>();
            var dialogs = await Client.Messages_GetAllDialogs();
            foreach (var dialog in dialogs.chats)
            {
                if (!dialog.Value.IsActive) continue;
                groups.Add(new ChatDto()
                {
                    Id = dialog.Value.ID,
                    Name = dialog.Value.Title,
                    Username = dialog.Value.MainUsername,
                    Type = dialog.Value.IsGroup ? "Group" : "Channel"
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
