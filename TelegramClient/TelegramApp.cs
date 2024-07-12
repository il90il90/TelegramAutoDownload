using BasePlugins;
using System;
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
    public partial class TelegramApp
    {
        public Func<ResultMessageEvent, ResultMessageEvent> OnUpdateResultMessage;
        public Func<ResultMessageEvent, ResultMessageEvent> OnErrorResultMessage;
        public readonly Client Client;
        private FactoryMessagesService factoryService;
        private FactoryUserService factoryUserService;

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
                    var task = Task.Run(async () =>
                    {
                        ResultExecute resultExecute = new ResultExecute(chat.Name);

                        if (updateNewMessage.message is Message infoMessage)
                        {
                            try
                            {
                                resultExecute = await factoryService.ExecuteAsync(updateNewMessage, chat);

                                if (resultExecute.IsSuccess && chat.ReactionIcon != null)
                                {
                                    if (updateNewMessage != null && !string.IsNullOrEmpty(chat.ReactionIcon))
                                    {
                                        await ReactToMessage(chat, updates, infoMessage, chat.ReactionIcon);
                                    }
                                }
                                OnUpdateResultMessage?.Invoke(new ResultMessageEvent
                                {
                                    Chat = chat,
                                    Message = infoMessage.message,
                                    PostAuthor = infoMessage.post_author,
                                    ResultExecute = resultExecute,
                                });
                            }
                            catch (Exception ex)
                            {
                                resultExecute.ErrorMessage = ex.Message;
                                OnErrorResultMessage?.Invoke(new ResultMessageEvent
                                {
                                    Chat = chat,
                                    Message = infoMessage.message,
                                    PostAuthor = infoMessage.post_author,
                                    ResultExecute = resultExecute,
                                });
                            }
                        }

                    });
                    tasks.Add(task);
                }
            }
            await Task.WhenAll(tasks);
        }

        private async Task ReactToMessage(ChatDto chatDto, UpdatesBase updates, Message message, string reactionIcon)
        {
            var isChannel = updates?.Chats?.FirstOrDefault().Value?.IsChannel;
            var isGroup = updates?.Chats?.FirstOrDefault().Value?.IsGroup;
            try
            {
                InputPeer inputPeer;

                if (updates.Chats.First().Value is TL.Channel channel)
                {
                    inputPeer = new InputPeerChannel(channel.ID, channel.access_hash);
                }
                else if (updates.Chats.First().Value is TL.Chat chat)
                {
                    inputPeer = new InputPeerChat(chat.ID);
                }
                else if (updates.Users.First().Value is User user)
                {
                    inputPeer = new InputPeerUser(user.id, user.access_hash);
                }
                else
                {
                    throw new InvalidOperationException($"reaction: Unknown peer type, isCahnnel: {isChannel}, isGroup: {isGroup}");
                }

                await Client.Messages_SendReaction(inputPeer, message.ID, new[] { new ReactionEmoji { emoticon = reactionIcon } });
            }
            catch (Exception e)
            {
                throw new Exception($"failed to send reaction {chatDto.Name} {e.Message} isCahnnel: {isChannel}, isGroup: {isGroup}", e);
            }
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
