using BasePlugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        public Func<ResultMessageEvent, Task<ResultMessageEvent>> OnSaved;
        public Func<ResultMessageEvent, Task<ResultMessageEvent>> OnWarnningMessage;
        public Func<ResultMessageEvent, Task<ResultMessageEvent>> OnErrorResultMessage;

        /// <summary>
        /// Fired during file downloads: (chatName, fileName, pluginName, percent, bytesDownloaded, totalBytes)
        /// </summary>
        public Action<string, string, string, double, long, long>? OnProgress { get; set; }

        /// <summary>
        /// Fired when a file download finishes: (chatName, fileName, success)
        /// </summary>
        public Action<string, string, bool>? OnComplete { get; set; }
        public readonly Client Client;
        private FactoryMessagesService factoryService;
        private FactoryUserService factoryUserService;
        private SemaphoreSlim _semaphore = new SemaphoreSlim(3);
        private readonly Task _loginTask;

        public TelegramApp(int appId, string apiHash)
        {
            // Store session in writable AppData folder so it survives installs/updates
            var sessionPath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "TelegramAutoDownload", "session.dat");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(sessionPath)!);
            Client = new Client(appId, apiHash, sessionPath);
            // Store the login task so callers can await it when needed
            _loginTask = Task.Run(async () =>
            {
                try { await Client.LoginUserIfNeeded(); }
                catch { /* login handled manually via Login() calls in LoginWindow */ }
            });
            Client.OnUpdates += Client_OnUpdates;
        }

        /// <summary>
        /// Waits for the background login task to complete (with optional timeout).
        /// </summary>
        public Task WaitForLoginAsync(int timeoutMs = 10000) =>
            Task.WhenAny(_loginTask, Task.Delay(timeoutMs)).ContinueWith(_ => { });

        /// <summary>
        /// Update the configuration for chat IDs and folder for file saving.
        /// </summary>
        /// <param name="chatIds">The list of chat IDs to update.</param>
        /// <param name="pathFolderToSaveFiles">The path to the folder where files will be saved.</param>
        public void UpdateConfig(ConfigParams configParams)
        {
            var chatIds = configParams.Chats?.Select(c => c.Id).ToList() ?? new List<long>();
            factoryService = new FactoryMessagesService(Client, configParams.PathSaveFile);
            factoryService.OnProgress = OnProgress;
            factoryService.OnComplete = OnComplete;
            factoryService.WireProgressCallbacks();
            factoryUserService = new FactoryUserService(chatIds, configParams);
            _semaphore = new SemaphoreSlim(Math.Max(1, configParams.DownloadThreads));
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
                        await _semaphore.WaitAsync();
                        try
                        {

                        ResultExecute resultExecute = new ResultExecute(chat.Name);

                        if (updateNewMessage.message is Message infoMessage)
                        {
                            try
                            {
                                // Skip messages older than the configured date filter
                                if (chat.DownloadAfterDate.HasValue
                                    && infoMessage.Date < chat.DownloadAfterDate.Value)
                                    return;

                                // Send "download starting" reaction before the download begins
                                if (!string.IsNullOrEmpty(chat.DownloadStartIcon))
                                {
                                    try { await ReactToMessage(chat, updates, infoMessage, chat.DownloadStartIcon); }
                                    catch { /* non-critical */ }
                                }

                                resultExecute = await factoryService.ExecuteAsync(updateNewMessage, chat);

                                var resultMessageEvent = new ResultMessageEvent
                                {
                                    Chat = chat,
                                    Message = infoMessage.message,
                                    PostAuthor = infoMessage.post_author,
                                    ResultExecute = resultExecute,
                                };
                                if (resultExecute.IsSuccess && chat.ReactionIcon != null && string.IsNullOrEmpty(resultExecute.ErrorMessage))
                                {
                                    if (updateNewMessage != null && !string.IsNullOrEmpty(chat.ReactionIcon))
                                    {
                                        try
                                        {
                                            await ReactToMessage(chat, updates, infoMessage, chat.ReactionIcon);
                                        }
                                        catch (Exception reactionEx)
                                        {
                                            // Reaction failure must not suppress the OnSaved notification
                                            resultExecute.ErrorMessage = reactionEx.Message;
                                        }
                                    }
                                    if (OnSaved != null)
                                        await OnSaved.Invoke(resultMessageEvent);
                                }
                                else
                                {
                                    if (!string.IsNullOrEmpty(resultExecute.ErrorMessage))
                                    {
                                        if (OnWarnningMessage != null)
                                            await OnWarnningMessage.Invoke(resultMessageEvent);
                                    }
                                }
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

                        }
                        finally
                        {
                            _semaphore.Release();
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

                if (updates.Chats.Count > 0 && updates.Chats.First().Value is TL.Channel channel)
                {
                    inputPeer = new InputPeerChannel(channel.ID, channel.access_hash);
                }
                else if (updates.Chats.Count > 0 && updates.Chats.First().Value is TL.Chat chat)
                {
                    inputPeer = new InputPeerChat(chat.ID);
                }
                else if (updates.Users.Count > 0 && updates.Users.First().Value is User user)
                {
                    inputPeer = new InputPeerUser(user.id, user.access_hash);
                }
                else
                {
                    throw new InvalidOperationException($"reaction: Unknown peer type, isChannel: {isChannel}, isGroup: {isGroup}");
                }

                await Client.Messages_SendReaction(inputPeer, message.ID, new[] { new ReactionEmoji { emoticon = reactionIcon } });
            }
            catch (Exception e)
            {
                throw new Exception($"failed to send reaction {chatDto.Name} {e.Message} isChannel: {isChannel}, isGroup: {isGroup}", e);
            }
        }

        /// <summary>
        /// Returns the display name of the currently logged-in account.
        /// Falls back to username, then phone number, then "Unknown".
        /// </summary>
        public string GetCurrentUserName()
        {
            var user = Client.User;
            if (user == null) return "Unknown";

            var name = $"{user.first_name} {user.last_name}".Trim();
            if (!string.IsNullOrWhiteSpace(name)) return name;
            if (!string.IsNullOrWhiteSpace(user.MainUsername)) return $"@{user.MainUsername}";
            if (!string.IsNullOrWhiteSpace(user.phone)) return user.phone;
            return "Unknown";
        }

        public async Task<IList<ChatDto>> GetAllChats()
        {
            var groups = new List<ChatDto>();

            // Fetch only one page (200 dialogs max) to avoid memory/freeze issues on
            // accounts with thousands of channels. null offset_peer = start from beginning.
            var dialogsBase = await Client.Messages_GetDialogs(
                offset_date: default,
                offset_id: 0,
                offset_peer: null!,
                limit: 200,
                hash: 0);

            // In WTelegram 4.1.1, Messages_DialogsSlice inherits Messages_Dialogs
            if (dialogsBase is not TL.Messages_Dialogs dlg) return groups;

            foreach (var kv in dlg.chats)
            {
                if (!kv.Value.IsActive) continue;
                groups.Add(new ChatDto()
                {
                    Id = kv.Value.ID,
                    Name = kv.Value.Title,
                    Username = kv.Value.MainUsername,
                    Type = kv.Value.IsGroup ? "Group" : "Channel"
                });
            }
            foreach (var kv in dlg.users)
            {
                if (kv.Value is not TL.User user) continue;
                groups.Add(new ChatDto()
                {
                    Id = user.ID,
                    Name = $"{user.first_name} {user.last_name}",
                    Username = user.MainUsername,
                    Type = "User"
                });
            }
            return groups;
        }
    }
}
