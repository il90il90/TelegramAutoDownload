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

        /// <summary>Fired when a file is queued (waiting for a download slot): (chatName, msgId, previewName)</summary>
        public Action<string, int, string>? OnEnqueued { get; set; }

        /// <summary>Fired when a queued item starts downloading: (chatName, msgId)</summary>
        public Action<string, int>? OnStarted { get; set; }
        public readonly Client Client;
        private FactoryMessagesService factoryService;
        private FactoryUserService factoryUserService;
        private SemaphoreSlim _semaphore = new SemaphoreSlim(3);
        private readonly Task _loginTask;

        // Stored config so we can look up monitored ChatDto objects by peer ID
        private ConfigParams? _configParams;

        // Cache of channel/chat access hashes populated from incoming updates
        // so we can construct InputPeer when processing missed history
        private readonly Dictionary<long, long> _accessHashes = new();

        // Per-chat watermark: highest message ID we have already queued for download.
        // Prevents re-downloading when we re-fetch history.
        private readonly Dictionary<long, int> _highWatermark = new();

        // Cached result of Messages_GetAvailableReactions (global list), shared across all chats that allow all reactions
        private List<string>? _cachedAllReactions;

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
            _configParams = configParams;
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

            // Cache access hashes for all channels/chats we see in any update
            foreach (var kv in updates.Chats)
            {
                if (kv.Value is TL.Channel ch)
                    _accessHashes[ch.ID] = ch.access_hash;
                else if (kv.Value is TL.Chat grp)
                    _accessHashes[grp.ID] = 0;
            }

            var chat = factoryUserService.Execute(updates);

            List<Task> tasks = [];
            foreach (Update update in updates.UpdateList)
            {
                if (update is UpdateChannelTooLong tooLong)
                {
                    // Telegram is telling us we missed updates — fetch history to catch up
                    var chatDto = FindMonitoredChat(tooLong.channel_id);
                    if (chatDto != null)
                        tasks.Add(Task.Run(() => ProcessMissedMessagesAsync(tooLong.channel_id, chatDto)));
                    continue;
                }

                if (chat == null) continue;

                if (update is UpdateNewMessage updateNewMessage)
                {
                    // Track watermark so catch-up doesn't re-download this message
                    if (updateNewMessage.message is Message liveMsg)
                    {
                        var peerId = liveMsg.peer_id is PeerChannel pc ? pc.channel_id
                                   : liveMsg.peer_id is PeerChat pg ? pg.chat_id
                                   : liveMsg.peer_id is PeerUser pu ? pu.user_id : 0;
                        if (peerId != 0)
                            _highWatermark[peerId] = Math.Max(
                                _highWatermark.GetValueOrDefault(peerId), liveMsg.ID);

                        // Register in queue immediately so the user can see it waiting
                        var previewName = GetPreviewFileName(liveMsg);
                        if (previewName != null)
                            OnEnqueued?.Invoke(chat.Name, liveMsg.ID, previewName);
                    }

                    var task = Task.Run(async () =>
                    {
                        await _semaphore.WaitAsync();
                        try
                        {

                        ResultExecute resultExecute = new ResultExecute(chat.Name);

                        if (updateNewMessage.message is Message infoMessage)
                        {
                            // Mark as actively downloading (was "Queued")
                            OnStarted?.Invoke(chat.Name, infoMessage.ID);

                            try
                            {

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

        /// <summary>
        /// Manually syncs all existing history for a chat — downloads every file that
        /// hasn't been downloaded yet. Called from the UI "Sync" button.
        /// </summary>
        public async Task SyncHistoryAsync(ChatDto chatDto, Action<string>? onStatus = null)
        {
            try
            {
                onStatus?.Invoke($"Syncing {chatDto.Name}…");

                // Reset watermark so we re-evaluate all messages
                _highWatermark[chatDto.Id] = 0;

                // Build InputPeer — try access hash cache first, then resolve via GetAllChats
                InputPeer? peer = null;
                if (_accessHashes.TryGetValue(chatDto.Id, out var hash))
                {
                    peer = hash != 0
                        ? new InputPeerChannel(chatDto.Id, hash)
                        : new InputPeerChat(chatDto.Id);
                }
                else
                {
                    // Try to resolve by fetching dialogs
                    var dialogs = await Client.Messages_GetDialogs(
                        offset_date: default, offset_id: 0, offset_peer: null!, limit: 200, hash: 0);
                    if (dialogs is TL.Messages_Dialogs dlg)
                    {
                        if (dlg.chats.TryGetValue(chatDto.Id, out var chatBase) && chatBase is TL.Channel ch)
                        {
                            _accessHashes[chatDto.Id] = ch.access_hash;
                            peer = new InputPeerChannel(chatDto.Id, ch.access_hash);
                        }
                        else if (dlg.chats.TryGetValue(chatDto.Id, out var grp) && grp is TL.Chat g)
                        {
                            _accessHashes[chatDto.Id] = 0;
                            peer = new InputPeerChat(chatDto.Id);
                        }
                    }
                }

                if (peer == null)
                {
                    onStatus?.Invoke($"Could not resolve peer for {chatDto.Name}");
                    return;
                }

                int totalQueued = 0;
                int offsetId = 0;
                const int pageSize = 100;

                while (true)
                {
                    var history = await Client.Messages_GetHistory(peer,
                        offset_id: offsetId, limit: pageSize);

                    var messages = history.Messages
                        .OfType<Message>()
                        .Where(m => m.media != null)
                        .ToList();

                    if (messages.Count == 0) break;

                    // Enqueue all messages for this page in the UI before starting downloads
                    foreach (var msg in messages)
                    {
                        var previewName = GetPreviewFileName(msg) ?? $"file_{msg.ID}";
                        OnEnqueued?.Invoke(chatDto.Name, msg.ID, previewName);
                    }

                    var tasks = messages.Select(msg => Task.Run(async () =>
                    {
                        await _semaphore.WaitAsync();
                        try
                        {
                            OnStarted?.Invoke(chatDto.Name, msg.ID);
                            var result = await factoryService.ExecuteDirectAsync(msg, chatDto);
                            // Only notify on genuine new downloads — not dedup skips (which have ErrorMessage set)
                            if (result.IsSuccess && string.IsNullOrEmpty(result.ErrorMessage) && OnSaved != null)
                                await OnSaved.Invoke(new ResultMessageEvent
                                {
                                    Chat = chatDto,
                                    Message = msg.message,
                                    PostAuthor = msg.post_author,
                                    ResultExecute = result,
                                });
                        }
                        finally { _semaphore.Release(); }
                    }));

                    await Task.WhenAll(tasks);
                    totalQueued += messages.Count;
                    onStatus?.Invoke($"Syncing {chatDto.Name}: {totalQueued} files queued…");

                    if (messages.Count < pageSize) break;
                    offsetId = messages.Min(m => m.ID);
                }

                onStatus?.Invoke($"Sync complete: {totalQueued} files from {chatDto.Name}");
            }
            catch (Exception ex)
            {
                onStatus?.Invoke($"Sync failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"SyncHistoryAsync failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Extracts a display filename from a message for queue preview.
        /// Returns null if the message has no downloadable media.
        /// </summary>
        private static string? GetPreviewFileName(Message msg)
        {
            if (msg.media is MessageMediaDocument { document: Document doc })
            {
                foreach (var attr in doc.attributes)
                {
                    if (attr is DocumentAttributeFilename fn && !string.IsNullOrEmpty(fn.file_name))
                        return fn.file_name;
                    if (attr is DocumentAttributeVideo)
                        return $"video_{msg.ID}.mp4";
                    if (attr is DocumentAttributeAudio audio)
                        return string.IsNullOrEmpty(audio.title) ? $"audio_{msg.ID}.mp3" : audio.title;
                }
                return $"file_{msg.ID}";
            }
            if (msg.media is MessageMediaPhoto)
                return $"photo_{msg.ID}.jpg";
            return null;
        }

        /// <summary>Returns the monitored ChatDto for a given peer ID, or null if not monitored.</summary>
        private ChatDto? FindMonitoredChat(long peerId) =>
            _configParams?.Chats?.FirstOrDefault(c => c.Id == peerId || c.Id == -peerId);

        /// <summary>
        /// Fetches recent history for a channel/chat and downloads any messages
        /// that arrived after the last live update we processed (using _highWatermark).
        /// Called when Telegram sends UpdateChannelTooLong.
        /// </summary>
        private async Task ProcessMissedMessagesAsync(long channelId, ChatDto chatDto)
        {
            try
            {
                if (!_accessHashes.TryGetValue(channelId, out var accessHash)) return;

                InputPeer peer = accessHash != 0
                    ? new InputPeerChannel(channelId, accessHash)
                    : new InputPeerChat(channelId);

                int watermark = _highWatermark.GetValueOrDefault(channelId, 0);

                // Paginate history newest-first until we reach the watermark
                int offsetId = 0;
                const int pageSize = 100;

                while (true)
                {
                    var history = await Client.Messages_GetHistory(peer,
                        offset_id: offsetId, limit: pageSize);

                    var messages = history.Messages
                        .OfType<Message>()
                        .Where(m => m.media != null && m.ID > watermark)
                        .ToList();

                    if (messages.Count == 0) break;

                    // Process each missed message through the download pipeline
                    var tasks = messages.Select(msg => Task.Run(async () =>
                    {
                        await _semaphore.WaitAsync();
                        try
                        {
                            // Mark as seen so live updates don't re-download it
                            _highWatermark[channelId] = Math.Max(
                                _highWatermark.GetValueOrDefault(channelId), msg.ID);

                            var result = await factoryService.ExecuteDirectAsync(msg, chatDto);
                            // Only notify on genuine new downloads — not dedup skips (which have ErrorMessage set)
                            if (result.IsSuccess && string.IsNullOrEmpty(result.ErrorMessage) && OnSaved != null)
                                await OnSaved.Invoke(new ResultMessageEvent
                                {
                                    Chat = chatDto,
                                    Message = msg.message,
                                    PostAuthor = msg.post_author,
                                    ResultExecute = result,
                                });
                        }
                        finally { _semaphore.Release(); }
                    }));

                    await Task.WhenAll(tasks);

                    // If we got a full page, continue paging; otherwise we're done
                    if (messages.Count < pageSize) break;
                    offsetId = messages.Min(m => m.ID);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ProcessMissedMessagesAsync failed for channel {channelId}: {ex.Message}");
            }
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

        /// <summary>
        /// Returns the list of emoji reactions available in the given chat.
        /// For channels/groups this reads the chat's configured reactions; for users all reactions are available.
        /// Falls back to a sensible default set on any error.
        /// </summary>
        public async Task<List<string>> GetChatAvailableReactionsAsync(ChatDto chatDto)
        {
            var fallback = new List<string> { "👍", "❤️", "🔥", "👌", "💯", "😂", "😮", "🎉" };
            try
            {
                // DMs support all available reactions
                if (chatDto.Type == "User")
                    return await GetAllAvailableReactionsAsync();

                if (!_accessHashes.TryGetValue(chatDto.Id, out var hash))
                    return fallback;

                TL.ChatReactions? available = null;

                if (hash != 0)
                {
                    // TL.Channel — channel or supergroup
                    var fullInfo = await Client.Channels_GetFullChannel(new TL.InputChannel(chatDto.Id, hash));
                    available = (fullInfo?.full_chat as TL.ChannelFull)?.available_reactions;
                }
                else
                {
                    // TL.Chat — regular group
                    var fullInfo = await Client.Messages_GetFullChat(chatDto.Id);
                    available = (fullInfo?.full_chat as TL.ChatFull)?.available_reactions;
                }

                if (available is TL.ChatReactionsSome some)
                {
                    var emojis = some.reactions
                        .OfType<TL.ReactionEmoji>()
                        .Select(r => r.emoticon)
                        .ToList();
                    return emojis.Count > 0 ? emojis : fallback;
                }

                if (available is TL.ChatReactionsAll)
                    return await GetAllAvailableReactionsAsync();

                // Unknown type (e.g. reactions disabled) — return empty
                return available != null ? new List<string>() : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        /// <summary>
        /// Returns the global list of standard emoji reactions, cached after the first call.
        /// </summary>
        private async Task<List<string>> GetAllAvailableReactionsAsync()
        {
            if (_cachedAllReactions != null) return _cachedAllReactions;
            try
            {
                var result = await Client.Messages_GetAvailableReactions(0);
                if (result is TL.Messages_AvailableReactions avail)
                {
                    // AvailableReaction.reaction is the emoji string directly in WTelegramClient 4.1.1
                    _cachedAllReactions = avail.reactions
                        .Select(r => r.reaction)
                        .Where(e => !string.IsNullOrEmpty(e))
                        .ToList();
                    return _cachedAllReactions;
                }
            }
            catch { }
            return new List<string> { "👍", "❤️", "🔥", "👌", "💯", "😂", "😮", "🎉" };
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

                // Cache access hashes so GetChatAvailableReactionsAsync can build InputPeer later
                if (kv.Value is TL.Channel tlChannel)
                    _accessHashes[tlChannel.ID] = tlChannel.access_hash;
                else if (kv.Value is TL.Chat tlChat)
                    _accessHashes[tlChat.ID] = 0;

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
