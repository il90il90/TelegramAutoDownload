using BasePlugins;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelegramAutoDownload.Models;
using TelegramClient.Factory.FactoriesMessages.Enum;
using TelegramClient.Factory.Service;
using TelegramClient.Models;
using TL;
using WTelegram;
using HistoryEntry = TelegramClient.Models.HistoryEntry;

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

        /// <summary>
        /// Fired when a file is silently skipped (already downloaded / dedup match): (chatName, msgId).
        /// The UI should remove the queued/downloading entry without showing an error.
        /// </summary>
        public Action<string, int>? OnSkipped { get; set; }

        /// <summary>
        /// Fired after a download failure so the UI layer can attach a Retry callback to the item.
        /// Arguments: (chatName, fileName, retryAction).
        /// </summary>
        public Action<string, string, Func<Task>>? OnRetryReady { get; set; }

        /// <summary>
        /// Fired for every incoming message when the chat has SaveHistory = true.
        /// Arguments: (chatDto, entry). The receiver should call ChatHistoryService.AppendEntryAsync.
        /// </summary>
        public Action<ChatDto, HistoryEntry>? OnHistoryEntry { get; set; }
        public readonly Client Client;
        private FactoryMessagesService factoryService;
        private FactoryUserService factoryUserService;
        private SemaphoreSlim _semaphore = new SemaphoreSlim(3);

        private readonly Task _loginTask;

        // Stored config so we can look up monitored ChatDto objects by peer ID
        private ConfigParams? _configParams;

        // Cache of channel/chat access hashes populated from incoming updates
        // so we can construct InputPeer when processing missed history.
        // ConcurrentDictionary because it is written from the update handler thread
        // AND read from background Task.Run threads concurrently.
        private readonly ConcurrentDictionary<long, long> _accessHashes = new();

        // Per-chat watermark: highest message ID we have already queued for download.
        // ConcurrentDictionary for the same reason — read/written from multiple threads.
        private readonly ConcurrentDictionary<long, int> _highWatermark = new();

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
            _loginTask = Task.Run(() => TelegramClientApiGate.RunAsync(async () =>
            {
                try { await Client.LoginUserIfNeeded().ConfigureAwait(false); }
                catch (Exception ex)
                {
                    // LoginWindow may complete login later; log for diagnostics only.
                    Log.Debug(ex, "Background LoginUserIfNeeded did not complete yet");
                }
            }));
            Client.OnUpdates += Client_OnUpdates;
            TelegramDownloadPerf.ConfigureClient(Client);
        }

        /// <summary>
        /// Waits for the background login task to complete (with optional timeout).
        /// </summary>
        public Task WaitForLoginAsync(int timeoutMs = 10000) =>
            Task.WhenAny(_loginTask, Task.Delay(timeoutMs)).ContinueWith(_ => { });

        /// <summary>
        /// Ensures the Telegram session is connected before any API call. Returns false if not logged in.
        /// </summary>
        public async Task<bool> EnsureTelegramReadyAsync()
        {
            try { await _loginTask.ConfigureAwait(false); }
            catch { /* background login may fail when manual login is required */ }

            if (Client.UserId != 0) return true;

            try
            {
                await TelegramClientApiGate.RunAsync(() => Client.LoginUserIfNeeded()).ConfigureAwait(false);
                return Client.UserId != 0;
            }
            catch (RpcException ex) when (ex.Code == 404 || ex.Message.Contains("AUTH", StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning(ex, "Telegram session invalid (auth key)");
                return false;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "EnsureTelegramReadyAsync failed");
                return false;
            }
        }

        /// <summary>
        /// Update the configuration for chat IDs and folder for file saving.
        /// </summary>
        /// <param name="chatIds">The list of chat IDs to update.</param>
        /// <param name="pathFolderToSaveFiles">The path to the folder where files will be saved.</param>
        /// <summary>
        /// Logs out the current Telegram session, deletes the local session file,
        /// and disposes the client. The caller should restart the application afterwards.
        /// </summary>
        public async Task LogoutAsync()
        {
            try
            {
                await TelegramClientApiGate.RunAsync(() => Client.Auth_LogOut()).ConfigureAwait(false);
            }
            catch { /* ignore network errors during logout */ }

            // Dispose client to release file locks on the session file
            try { Client.Dispose(); } catch { }

            // Delete the session file so the next startup triggers re-authentication
            var sessionPath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "TelegramAutoDownload", "session.dat");
            try { System.IO.File.Delete(sessionPath); } catch { }
        }

        public void UpdateConfig(ConfigParams configParams)
        {
            _configParams = configParams;
            var chatIds = MonitoredChatHelper.GetMonitoredChatIds(configParams.Chats);
            factoryService = new FactoryMessagesService(Client, configParams.PathSaveFile ?? string.Empty);
            factoryService.OnProgress = OnProgress;
            factoryService.OnComplete = OnComplete;
            factoryService.RefreshMessage = RefreshMessageAsync;
            factoryService.WireProgressCallbacks();
            factoryUserService = new FactoryUserService(chatIds, configParams);
            TelegramDownloadPerf.ConfigureClient(Client);
            _semaphore = new SemaphoreSlim(TelegramDownloadPerf.ClampDownloadThreads(configParams.DownloadThreads));

            // Clean up stale .part files in the background so startup is not blocked.
            // Files older than 7 days are treated as abandoned (resume is unlikely after that long).
            if (!string.IsNullOrEmpty(configParams.PathSaveFile))
                _ = Task.Run(() => PartFileCleanup.CleanStaleParts(configParams.PathSaveFile));
        }

        /// <summary>
        /// Re-fetches a single message from Telegram to obtain a fresh file reference.
        /// Called automatically when FILE_REFERENCE_EXPIRED is encountered during download.
        /// </summary>
        private Task<Message?> RefreshMessageAsync(ChatDto chatDto, int msgId) =>
            TelegramClientApiGate.RunAsync(() => RefreshMessageCoreAsync(chatDto, msgId));

        private async Task<Message?> RefreshMessageCoreAsync(ChatDto chatDto, int msgId)
        {
            try
            {
                if (!await EnsureTelegramReadyAsync().ConfigureAwait(false))
                    return null;

                _accessHashes.TryGetValue(chatDto.Id, out var accessHash);
                MessageBase[]? messages;

                if (accessHash != 0)
                {
                    var result = await Client.Channels_GetMessages(
                        new InputChannel(chatDto.Id, accessHash),
                        new InputMessageID { id = msgId }).ConfigureAwait(false);
                    messages = result?.Messages;
                }
                else
                {
                    var result = await Client.Messages_GetMessages(new InputMessageID { id = msgId })
                        .ConfigureAwait(false);
                    messages = result?.Messages;
                }

                return messages?.OfType<Message>().FirstOrDefault();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "RefreshMessageAsync failed for msg {MsgId} in chat {ChatName}", msgId, chatDto.Name);
                return null;
            }
        }
        private async Task Client_OnUpdates(UpdatesBase updates)
        {
            if (factoryService == null || _configParams == null)
                return;

            // Cache access hashes for all channels/chats we see in any update
            foreach (var kv in updates.Chats)
            {
                if (kv.Value is TL.Channel ch)
                    _accessHashes[ch.ID] = ch.access_hash;
                else if (kv.Value is TL.Chat grp)
                    _accessHashes[grp.ID] = 0;
            }

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

                if (update is not UpdateNewMessage updateNewMessage) continue;
                if (updateNewMessage.message is not Message liveMsg) continue;

                // Resolve settings per message peer — never reuse one chat for an entire update batch.
                var chat = FindMonitoredChat(MonitoredChatHelper.GetPeerId(liveMsg));
                if (chat == null) continue;

                var peerId = MonitoredChatHelper.GetPeerId(liveMsg);
                if (peerId != 0)
                    _highWatermark.AddOrUpdate(peerId, liveMsg.ID, (_, prev) => Math.Max(prev, liveMsg.ID));

                // Non-null when a text-only message matches a Filter regex pattern.
                string? capturedTextPreview = null;

                var previewName = GetPreviewFileName(liveMsg);
                var typeEnabled = IsDownloadTypeEnabledForMessage(liveMsg, chat);
                if (previewName != null && typeEnabled)
                    OnEnqueued?.Invoke(chat.Name, liveMsg.ID, previewName);

                if (previewName == null
                    && !string.IsNullOrEmpty(liveMsg.message)
                    && chat.IgnoreFileByRegex.Count > 0)
                {
                    foreach (var pattern in chat.IgnoreFileByRegex)
                    {
                        if (System.Text.RegularExpressions.Regex.IsMatch(
                                liveMsg.message, pattern,
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        {
                            var snippet = liveMsg.message.Length > 45
                                ? liveMsg.message[..45] + "…"
                                : liveMsg.message;
                            capturedTextPreview = $"📝 {snippet}";
                            OnEnqueued?.Invoke(chat.Name, liveMsg.ID, capturedTextPreview);
                            break;
                        }
                    }
                }

                var capturedChat = chat;
                var textPreviewCapture = capturedTextPreview;
                var sem = _semaphore;
                var task = Task.Run(async () =>
                {
                    if (updateNewMessage.message is not Message infoMessage)
                        return;

                    // History + history reaction — do not hold a download slot
                    if (capturedChat.SaveHistory && OnHistoryEntry != null)
                    {
                        try
                        {
                            var entry = ChatHistoryService.CreateEntry(infoMessage);
                            OnHistoryEntry.Invoke(capturedChat, entry);
                            if (!string.IsNullOrEmpty(capturedChat.HistoryIcon))
                            {
                                try { await ReactToMessage(capturedChat, updates, infoMessage, capturedChat.HistoryIcon); }
                                catch { /* non-critical */ }
                            }
                        }
                        catch { /* history write must never break downloads */ }
                    }

                    var mediaPreview = GetPreviewFileName(infoMessage);
                    var mediaEnabled = mediaPreview != null
                        && IsDownloadTypeEnabledForMessage(infoMessage, capturedChat);
                    var hasUrl = !string.IsNullOrEmpty(infoMessage.message)
                        && infoMessage.message.Contains("http", StringComparison.OrdinalIgnoreCase);
                    var hasQueuedItem = mediaEnabled || textPreviewCapture != null;
                    var shouldRunPipeline = textPreviewCapture != null || mediaEnabled || hasUrl;
                    if (!shouldRunPipeline)
                        return;

                    if (hasQueuedItem)
                        OnStarted?.Invoke(capturedChat.Name, infoMessage.ID);

                    if (hasQueuedItem && !string.IsNullOrEmpty(capturedChat.DownloadStartIcon))
                    {
                        try { await ReactToMessage(capturedChat, updates, infoMessage, capturedChat.DownloadStartIcon); }
                        catch { /* non-critical */ }
                    }

                    var resultExecute = new ResultExecute(capturedChat.Name);
                    try
                    {
                        await sem.WaitAsync();
                        try
                        {
                            if (textPreviewCapture != null && mediaPreview == null)
                                resultExecute = await SaveCapturedTextAsync(infoMessage, capturedChat);
                            else
                                resultExecute = await factoryService.ExecuteAsync(updateNewMessage, capturedChat);
                        }
                        finally
                        {
                            sem.Release();
                        }

                        var resultMessageEvent = new ResultMessageEvent
                        {
                            Chat = capturedChat,
                            Message = infoMessage.message,
                            PostAuthor = infoMessage.post_author,
                            ResultExecute = resultExecute,
                        };

                        if (resultExecute.IsSuccess && string.IsNullOrEmpty(resultExecute.ErrorMessage))
                        {
                            if (!string.IsNullOrEmpty(capturedChat.ReactionIcon))
                            {
                                try { await ReactToMessage(capturedChat, updates, infoMessage, capturedChat.ReactionIcon); }
                                catch (Exception reactionEx) { resultExecute.ErrorMessage = reactionEx.Message; }
                            }
                            if (OnSaved != null)
                                await OnSaved.Invoke(resultMessageEvent);
                        }
                        else if (resultExecute.IsSuccess && !string.IsNullOrEmpty(resultExecute.ErrorMessage))
                        {
                            OnSkipped?.Invoke(capturedChat.Name, infoMessage.ID);
                        }
                        else if (!string.IsNullOrEmpty(resultExecute.ErrorMessage))
                        {
                            if (OnWarnningMessage != null)
                                await OnWarnningMessage.Invoke(resultMessageEvent);

                            if (!resultExecute.IsSuccess && !string.IsNullOrEmpty(resultExecute.FileName))
                            {
                                var capturedUpdate = updateNewMessage;
                                var capturedSem = sem;
                                OnRetryReady?.Invoke(capturedChat.Name, resultExecute.FileName, async () =>
                                {
                                    await capturedSem.WaitAsync();
                                    try
                                    {
                                        OnStarted?.Invoke(capturedChat.Name, infoMessage.ID);
                                        var retryResult = await factoryService.ExecuteAsync(capturedUpdate, capturedChat);
                                        if (retryResult.IsSuccess && string.IsNullOrEmpty(retryResult.ErrorMessage) && OnSaved != null)
                                            await OnSaved.Invoke(new ResultMessageEvent
                                            {
                                                Chat = capturedChat,
                                                Message = infoMessage.message,
                                                PostAuthor = infoMessage.post_author,
                                                ResultExecute = retryResult,
                                            });
                                    }
                                    finally { capturedSem.Release(); }
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        resultExecute.ErrorMessage = ex.Message;
                        Log.Error(ex, "Message pipeline failed for chat {Chat} msgId {MsgId}", capturedChat.Name, infoMessage.ID);
                        OnComplete?.Invoke(capturedChat.Name, GetPreviewFileName(infoMessage) ?? $"file_{infoMessage.ID}", false);
                        OnErrorResultMessage?.Invoke(new ResultMessageEvent
                        {
                            Chat = capturedChat,
                            Message = infoMessage.message,
                            PostAuthor = infoMessage.post_author,
                            ResultExecute = resultExecute,
                        });
                    }
                });
                tasks.Add(task);
            }
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Manually syncs all existing history for a chat — downloads every file that
        /// hasn't been downloaded yet. Called from the UI "Sync" button.
        /// </summary>
        public Task SyncHistoryAsync(ChatDto chatDto, Action<string>? onStatus = null) =>
            TelegramClientApiGate.RunAsync(() => SyncHistoryCoreAsync(chatDto, onStatus));

        private async Task SyncHistoryCoreAsync(ChatDto chatDto, Action<string>? onStatus = null)
        {
            try
            {
                if (!await EnsureTelegramReadyAsync().ConfigureAwait(false))
                {
                    onStatus?.Invoke("Not connected to Telegram. Log out and sign in again.");
                    return;
                }

                onStatus?.Invoke($"Syncing {chatDto.Name}…");

                _highWatermark[chatDto.Id] = 0;

                var peer = await ResolveInputPeerAsync(chatDto).ConfigureAwait(false);
                if (peer == null)
                {
                    onStatus?.Invoke($"Could not resolve peer for {chatDto.Name}. Click Refresh, then try again.");
                    return;
                }

                int totalQueued = 0;
                int offsetId = 0;
                const int pageSize = 100;

                while (true)
                {
                    var history = await Client.Messages_GetHistory(peer,
                        offset_id: offsetId, limit: pageSize);

                    // Use the TOTAL message count (including MessageService and MessageEmpty) for
                    // pagination control. A page that contains only service messages would have
                    // rawMessages.Count == 0 even though Telegram returned a full page, causing
                    // the loop to stop early and miss older messages.
                    if (history.Messages.Length == 0) break;

                    var rawMessages = history.Messages.OfType<Message>().ToList();

                    // Native media that matches this chat's download settings (Videos, Photos, Music, Files)
                    var mediaMessages = rawMessages
                        .Where(m => m.media != null && IsMessageTypeEnabled(m, chatDto))
                        .ToList();

                    // Text messages that contain URLs — handled by plugins (YouTube, SocialMedia, Torrent, etc.)
                    // These are processed silently without a UI queue entry because we can't know
                    // upfront whether any plugin will actually handle the URL.
                    var urlMessages = rawMessages
                        .Where(m => !mediaMessages.Contains(m) &&
                                    !string.IsNullOrEmpty(m.message) &&
                                    m.message.Contains("http", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    // Text-only messages that match a Filter regex pattern → save as .txt capture file.
                    var textCaptureMessages = chatDto.IgnoreFileByRegex.Count > 0
                        ? rawMessages
                            .Where(m => m.media == null &&
                                        !urlMessages.Contains(m) &&
                                        !string.IsNullOrEmpty(m.message) &&
                                        chatDto.IgnoreFileByRegex.Any(p =>
                                            System.Text.RegularExpressions.Regex.IsMatch(
                                                m.message, p,
                                                System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                            .ToList()
                        : [];

                    // Enqueue native media messages + text captures in the UI
                    foreach (var msg in mediaMessages)
                    {
                        var previewName = GetPreviewFileName(msg) ?? $"file_{msg.ID}";
                        OnEnqueued?.Invoke(chatDto.Name, msg.ID, previewName);
                    }
                    foreach (var msg in textCaptureMessages)
                    {
                        var snippet = msg.message!.Length > 45 ? msg.message[..45] + "…" : msg.message;
                        OnEnqueued?.Invoke(chatDto.Name, msg.ID, $"📝 {snippet}");
                    }

                    var sem = _semaphore; // Capture before lambda — see Client_OnUpdates for explanation

                    // Process native media with full UI lifecycle (queued → downloading → complete)
                    var mediaTasks = mediaMessages.Select(msg => Task.Run(async () =>
                    {
                        await sem.WaitAsync();
                        try
                        {
                            OnStarted?.Invoke(chatDto.Name, msg.ID);
                            var result = await factoryService.ExecuteDirectAsync(msg, chatDto);

                            if (result.IsSuccess && !string.IsNullOrEmpty(result.ErrorMessage))
                            {
                                // Dedup skip — remove the UI entry silently
                                OnSkipped?.Invoke(chatDto.Name, msg.ID);
                            }
                            else if (result.IsSuccess && string.IsNullOrEmpty(result.ErrorMessage) && OnSaved != null)
                            {
                                await OnSaved.Invoke(new ResultMessageEvent
                                {
                                    Chat = chatDto,
                                    Message = msg.message,
                                    PostAuthor = msg.post_author,
                                    ResultExecute = result,
                                });
                            }
                        }
                        finally { sem.Release(); }
                    }));

                    // Process URL messages silently — no UI queue entry, notification only on success
                    var urlTasks = urlMessages.Select(msg => Task.Run(async () =>
                    {
                        await sem.WaitAsync();
                        try
                        {
                            var result = await factoryService.ExecuteDirectAsync(msg, chatDto);
                            if (result.IsSuccess && string.IsNullOrEmpty(result.ErrorMessage) && OnSaved != null)
                            {
                                await OnSaved.Invoke(new ResultMessageEvent
                                {
                                    Chat = chatDto,
                                    Message = msg.message,
                                    PostAuthor = msg.post_author,
                                    ResultExecute = result,
                                });
                            }
                        }
                        finally { sem.Release(); }
                    }));

                    // Process text captures — save as .txt files with full UI lifecycle
                    var textCaptureTasks = textCaptureMessages.Select(msg => Task.Run(async () =>
                    {
                        await sem.WaitAsync();
                        try
                        {
                            OnStarted?.Invoke(chatDto.Name, msg.ID);
                            var result = await SaveCapturedTextAsync(msg, chatDto);
                            if (result.IsSuccess && string.IsNullOrEmpty(result.ErrorMessage) && OnSaved != null)
                            {
                                await OnSaved.Invoke(new ResultMessageEvent
                                {
                                    Chat = chatDto,
                                    Message = msg.message,
                                    PostAuthor = msg.post_author,
                                    ResultExecute = result,
                                });
                            }
                        }
                        finally { sem.Release(); }
                    }));

                    await Task.WhenAll(mediaTasks.Concat(urlTasks).Concat(textCaptureTasks));
                    totalQueued += mediaMessages.Count + textCaptureMessages.Count;
                    onStatus?.Invoke($"Syncing {chatDto.Name}: {totalQueued} files queued…");

                    // Stop when the API returns fewer items than requested (beginning of chat).
                    // Use the min ID across ALL message types (MessageService, MessageEmpty included)
                    // so service-message-heavy pages don't create ID gaps.
                    if (history.Messages.Length < pageSize) break;
                    offsetId = history.Messages.Select(m => m.ID).Min();
                }

                onStatus?.Invoke($"Sync complete: {totalQueued} files from {chatDto.Name}");
            }
            catch (Exception ex)
            {
                onStatus?.Invoke($"Sync failed: {ex.Message}");
                Log.Error(ex, "SyncHistoryAsync failed for chat {ChatName}", chatDto.Name);
            }
        }

        /// <summary>
        /// Resolves a chat/channel/user to an <see cref="InputPeer"/> for history API calls.
        /// Paginates all dialog folders (main + archive) and falls back to username resolution.
        /// </summary>
        private async Task<InputPeer?> ResolveInputPeerAsync(ChatDto chatDto)
        {
            if (TryBuildInputPeerFromCache(chatDto, out var peer))
                return peer;

            await TryCacheAccessHashFromDialogsAsync(chatDto.Id).ConfigureAwait(false);
            if (TryBuildInputPeerFromCache(chatDto, out peer))
                return peer;

            if (!string.IsNullOrWhiteSpace(chatDto.Username))
            {
                await TryCacheAccessHashFromUsernameAsync(chatDto.Username).ConfigureAwait(false);
                if (TryBuildInputPeerFromCache(chatDto, out peer))
                    return peer;
            }

            return null;
        }

        private bool TryBuildInputPeerFromCache(ChatDto chatDto, out InputPeer? peer)
        {
            peer = null;
            if (!_accessHashes.TryGetValue(chatDto.Id, out var hash))
                return false;
            peer = BuildInputPeer(chatDto, hash);
            return true;
        }

        internal static InputPeer BuildInputPeer(ChatDto chatDto, long accessHash)
        {
            if (string.Equals(chatDto.Type, "User", StringComparison.OrdinalIgnoreCase))
                return new InputPeerUser(chatDto.Id, accessHash);
            // Supergroups and broadcast channels use a non-zero access_hash; legacy basic groups use 0.
            if (accessHash != 0)
                return new InputPeerChannel(chatDto.Id, accessHash);
            return new InputPeerChat(chatDto.Id);
        }

        /// <summary>
        /// Walks main and archive dialog lists until <paramref name="chatId"/> is found and cached.
        /// </summary>
        private async Task<bool> TryCacheAccessHashFromDialogsAsync(long chatId)
        {
            foreach (int folderId in new[] { 0, 1 })
            {
                int offsetId = 0;
                DateTime offsetDate = default;
                InputPeer offsetPeer = null!;
                const int pageSize = 100;
                const int maxPages = 8; // fallback lookup only — full list uses FetchDialogFolder

                for (int page = 0; page < maxPages; page++)
                {
                    var result = await Client.Messages_GetDialogs(
                        offset_date: offsetDate,
                        offset_id: offsetId,
                        offset_peer: offsetPeer,
                        limit: pageSize,
                        hash: 0,
                        folder_id: folderId).ConfigureAwait(false);

                    Dialog[] pageDialogs;
                    MessageBase[] pageMessages;
                    Dictionary<long, ChatBase> pageChats;
                    Dictionary<long, User> pageUsers;
                    bool isLastPage;

                    if (result is TL.Messages_DialogsSlice slice)
                    {
                        pageDialogs  = slice.dialogs.OfType<Dialog>().ToArray();
                        pageMessages = slice.messages;
                        pageChats    = slice.chats;
                        pageUsers    = slice.users;
                        isLastPage   = pageDialogs.Length < pageSize;
                    }
                    else if (result is TL.Messages_Dialogs full)
                    {
                        pageDialogs  = full.dialogs.OfType<Dialog>().ToArray();
                        pageMessages = full.messages;
                        pageChats    = full.chats;
                        pageUsers    = full.users;
                        isLastPage   = true;
                    }
                    else break;

                    if (pageChats.TryGetValue(chatId, out var chatBase))
                    {
                        if (chatBase is TL.Channel ch)
                            _accessHashes[chatId] = ch.access_hash;
                        else if (chatBase is TL.Chat)
                            _accessHashes[chatId] = 0;
                        return true;
                    }

                    if (pageUsers.TryGetValue(chatId, out var userBase) && userBase is TL.User u)
                    {
                        _accessHashes[chatId] = u.access_hash;
                        return true;
                    }

                    if (isLastPage || pageDialogs.Length == 0) break;

                    var lastDialog = pageDialogs[^1];
                    offsetId = lastDialog.top_message;
                    var topMsg = pageMessages.OfType<Message>()
                        .FirstOrDefault(m => m.ID == lastDialog.top_message);
                    if (topMsg == null) break;
                    offsetDate = topMsg.Date;

                    var peerId = lastDialog.peer;
                    if (pageChats.TryGetValue(peerId, out var peerChatBase))
                    {
                        offsetPeer = peerChatBase is Channel peerChannel
                            ? new InputPeerChannel(peerId, peerChannel.access_hash)
                            : (InputPeer)new InputPeerChat(peerId);
                    }
                    else if (pageUsers.TryGetValue(peerId, out var peerUserBase) && peerUserBase is User peerUser)
                        offsetPeer = new InputPeerUser(peerId, peerUser.access_hash);
                    else break;
                }
            }

            return _accessHashes.ContainsKey(chatId);
        }

        private async Task TryCacheAccessHashFromUsernameAsync(string username)
        {
            var handle = username.Trim().TrimStart('@');
            if (handle.Length == 0) return;

            try
            {
                var resolved = await Client.Contacts_ResolveUsername(handle).ConfigureAwait(false);
                foreach (var kv in resolved.chats)
                {
                    if (kv.Value is TL.Channel ch)
                        _accessHashes[ch.ID] = ch.access_hash;
                    else if (kv.Value is TL.Chat grp)
                        _accessHashes[grp.ID] = 0;
                }

                foreach (var kv in resolved.users)
                {
                    if (kv.Value is TL.User u)
                        _accessHashes[u.ID] = u.access_hash;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Contacts_ResolveUsername failed for @{Username}", handle);
            }
        }

        /// <summary>
        /// Fetches the most recent <paramref name="count"/> messages from Telegram for a chat.
        /// Returns them newest-first. Returns an empty list if the peer cannot be resolved
        /// (e.g. not yet connected) or on error.
        /// </summary>
        public Task<List<HistoryEntry>> GetRecentMessagesAsync(ChatDto chatDto, int count = 50) =>
            TelegramClientApiGate.RunAsync(() => GetRecentMessagesCoreAsync(chatDto, count));

        private async Task<List<HistoryEntry>> GetRecentMessagesCoreAsync(ChatDto chatDto, int count)
        {
            try
            {
                if (!await EnsureTelegramReadyAsync().ConfigureAwait(false))
                    return [];

                var peer = await ResolveInputPeerAsync(chatDto).ConfigureAwait(false);
                if (peer == null) return [];

                var history = await Client.Messages_GetHistory(peer, limit: count);
                var result  = new List<HistoryEntry>();

                // Extract users dictionary from the concrete history result type
                Dictionary<long, User>? users = null;
                if      (history is TL.Messages_Messages       m1) users = m1.users;
                else if (history is TL.Messages_MessagesSlice  m2) users = m2.users;
                else if (history is TL.Messages_ChannelMessages m3) users = m3.users;
                users ??= [];

                foreach (var msg in history.Messages)
                {
                    if (msg is not TL.Message m) continue;
                    var senderId = m.from_id is PeerUser pu2 ? pu2.user_id : chatDto.Id;
                    var entry = new HistoryEntry
                    {
                        Id         = m.ID,
                        Date       = new DateTimeOffset(m.Date),
                        SenderId   = senderId,
                        SenderName = users.TryGetValue(senderId, out var sender)
                                        ? (sender.MainUsername ?? sender.first_name) : null,
                        Text       = m.message ?? string.Empty,
                        MediaType  = m.media switch
                        {
                            MessageMediaPhoto     => "Photo",
                            MessageMediaDocument  => "Document",
                            _                     => null,
                        },
                        FileName   = m.media is MessageMediaDocument { document: Document doc }
                                        ? doc.Filename : null,
                    };
                    result.Add(entry);
                }

                return result;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GetRecentMessagesAsync failed for chat {ChatName}", chatDto.Name);
                return [];
            }
        }

        /// <summary>One page of messages for the manual browse window (newest first when <paramref name="offsetId"/> is 0).</summary>
        public Task<(IReadOnlyList<Message> Messages, int NextOffsetId, bool HasMore, string? ErrorMessage)>
            FetchBrowseHistoryPageAsync(ChatDto chatDto, int offsetId, int pageSize = 50) =>
            TelegramClientApiGate.RunAsync(() => FetchBrowseHistoryPageCoreAsync(chatDto, offsetId, pageSize));

        private async Task<(IReadOnlyList<Message> Messages, int NextOffsetId, bool HasMore, string? ErrorMessage)>
            FetchBrowseHistoryPageCoreAsync(ChatDto chatDto, int offsetId, int pageSize)
        {
            try
            {
                if (!await EnsureTelegramReadyAsync().ConfigureAwait(false))
                {
                    return (Array.Empty<Message>(), offsetId, false,
                        "Not connected to Telegram. Log out and sign in again, then click Refresh.");
                }

                var peer = await ResolveInputPeerAsync(chatDto).ConfigureAwait(false);
                if (peer == null)
                {
                    return (Array.Empty<Message>(), offsetId, false,
                        "Could not resolve this chat on Telegram. Click Refresh on the main window, then try again.");
                }

                var currentOffset = offsetId;
                const int maxServiceOnlySkips = 8;
                for (int skip = 0; skip <= maxServiceOnlySkips; skip++)
                {
                    var history = await Client.Messages_GetHistory(peer, offset_id: currentOffset, limit: pageSize)
                        .ConfigureAwait(false);
                    if (history.Messages.Length == 0)
                        return (Array.Empty<Message>(), currentOffset, false, null);

                    var page = history.Messages.OfType<Message>().ToList();
                    var nextOffset = history.Messages.Min(m => m.ID);
                    var hasMore = history.Messages.Length >= pageSize;

                    if (page.Count > 0)
                        return (page, nextOffset, hasMore, null);

                    if (!hasMore)
                        return (page, nextOffset, false, null);

                    currentOffset = nextOffset;
                }

                return (Array.Empty<Message>(), currentOffset, true,
                    "Only system messages in this range — use Load older to continue.");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "FetchBrowseHistoryPageAsync failed for chat {ChatName}", chatDto.Name);
                return (Array.Empty<Message>(), offsetId, false, ex.Message);
            }
        }

        /// <summary>
        /// Downloads the given messages using the same pipeline as Sync (media, URL plugins, filter text capture).
        /// When <paramref name="forBrowseWindow"/> is true, uses a relaxed <see cref="ChatDto"/> clone so manual
        /// picks work even if the chat is not monitored (Selected) or download-type toggles are off.
        /// </summary>
        public async Task ManualDownloadMessagesAsync(
            ChatDto chatDto,
            IEnumerable<Message> messages,
            bool forBrowseWindow = false)
        {
            if (factoryService == null)
                throw new InvalidOperationException("Telegram client is not configured yet.");

            var execDto = forBrowseWindow ? CloneChatDtoForBrowseManualDownload(chatDto) : chatDto;

            foreach (var msg in messages)
            {
                await _semaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    var preview = GetPreviewFileName(msg);
                    if (preview != null)
                        OnEnqueued?.Invoke(chatDto.Name, msg.ID, preview);

                    OnStarted?.Invoke(chatDto.Name, msg.ID);

                    ResultExecute result;
                    var isTextCapture = preview == null && msg.media == null && chatDto.IgnoreFileByRegex.Count > 0 &&
                        !string.IsNullOrEmpty(msg.message) &&
                        chatDto.IgnoreFileByRegex.Any(p =>
                            System.Text.RegularExpressions.Regex.IsMatch(
                                msg.message, p,
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase));

                    if (isTextCapture)
                        result = await SaveCapturedTextAsync(msg, chatDto).ConfigureAwait(false);
                    else
                        result = await factoryService.ExecuteDirectAsync(msg, execDto).ConfigureAwait(false);

                    // Browse window: plain text (no URL) that no plugin handled — save under …/Messages like filter capture.
                    if (forBrowseWindow && !result.IsSuccess && preview == null && msg.media == null && !isTextCapture &&
                        !string.IsNullOrWhiteSpace(msg.message) &&
                        !msg.message.Contains("http", StringComparison.OrdinalIgnoreCase))
                        result = await SaveCapturedTextAsync(msg, chatDto).ConfigureAwait(false);

                    if (result.IsSuccess && !string.IsNullOrEmpty(result.ErrorMessage))
                        OnSkipped?.Invoke(chatDto.Name, msg.ID);
                    else if (result.IsSuccess && string.IsNullOrEmpty(result.ErrorMessage) && OnSaved != null)
                    {
                        await OnSaved.Invoke(new ResultMessageEvent
                        {
                            Chat          = chatDto,
                            Message       = msg.message ?? string.Empty,
                            PostAuthor    = msg.post_author,
                            ResultExecute = result,
                        }).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _semaphore.Release();
                }
            }
        }

        /// <summary>
        /// Copy of <see cref="ChatDto"/> with all native download types on and URL plugins enabled when the key was missing.
        /// Does not mutate the original config row.
        /// </summary>
        private static ChatDto CloneChatDtoForBrowseManualDownload(ChatDto source)
        {
            var plugins = new Dictionary<string, bool>(
                source.EnabledPlugins,
                StringComparer.OrdinalIgnoreCase);
            foreach (var key in new[] { "SocialMedia", "YouTube", "Other", "Torrent" })
            {
                if (!plugins.ContainsKey(key))
                    plugins[key] = true;
            }

            return new ChatDto
            {
                Id               = source.Id,
                Name             = source.Name,
                Username         = source.Username,
                Type             = source.Type,
                Selected         = source.Selected,
                NameLower        = source.NameLower,
                UsernameLower    = source.UsernameLower,
                ReactionIcon     = source.ReactionIcon,
                DownloadStartIcon = source.DownloadStartIcon,
                MembersCount     = source.MembersCount,
                Muted            = source.Muted,
                Download         = new Download { Videos = true, Photos = true, Music = true, Files = true },
                DownloadFromSize = source.DownloadFromSize,
                IgnoreFileByRegex = new List<string>(source.IgnoreFileByRegex),
                EnabledPlugins   = plugins,
                YtdlpQuality     = source.YtdlpQuality,
                FolderTemplate   = source.FolderTemplate,
                SocialDownloadFolderTemplate = source.SocialDownloadFolderTemplate,
                YoutubeDownloadFolderTemplate = source.YoutubeDownloadFolderTemplate,
                OtherDownloadFolderTemplate = source.OtherDownloadFolderTemplate,
                TorrentDownloadFolderTemplate = source.TorrentDownloadFolderTemplate,
                SaveHistory      = source.SaveHistory,
                HistoryIcon      = source.HistoryIcon,
                AvailableReactions = source.AvailableReactions != null
                    ? new List<string>(source.AvailableReactions)
                    : null,
            };
        }

        /// <summary>
        /// Fetches all members of a group, supergroup, or channel.
        /// Reports progress via <paramref name="onProgress"/> (fetched, total).
        /// Returns an empty list when the peer cannot be resolved or access is denied.
        /// </summary>
        public Task<List<MemberEntry>> GetChannelMembersAsync(
            ChatDto chatDto,
            Action<int, int>? onProgress = null,
            CancellationToken cancellationToken = default) =>
            TelegramClientApiGate.RunAsync(() =>
                GetChannelMembersCoreAsync(chatDto, onProgress, cancellationToken));

        private async Task<List<MemberEntry>> GetChannelMembersCoreAsync(
            ChatDto chatDto,
            Action<int, int>? onProgress,
            CancellationToken cancellationToken)
        {
            var result = new List<MemberEntry>();
            try
            {
                if (!await EnsureTelegramReadyAsync().ConfigureAwait(false))
                    return result;

                await ResolveInputPeerAsync(chatDto).ConfigureAwait(false);
                if (!_accessHashes.TryGetValue(chatDto.Id, out var hash))
                    return result;

                if (hash != 0)
                {
                    // Channel or supergroup — use Channels_GetParticipants (paginated)
                    var inputChannel = new TL.InputChannel(chatDto.Id, hash);
                    const int batchSize = 200;
                    int offset = 0;
                    int total  = 0;

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var batch = await Client.Channels_GetParticipants(
                            inputChannel,
                            new TL.ChannelParticipantsSearch { q = "" },
                            offset, batchSize, hash: 0);

                        if (total == 0)
                            total = batch.count;

                        if (batch.participants.Length == 0)
                            break;

                        foreach (var p in batch.participants)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var userId = p switch
                            {
                                TL.ChannelParticipant          cp  => cp.user_id,
                                TL.ChannelParticipantSelf      cps => cps.user_id,
                                TL.ChannelParticipantAdmin     cpa => cpa.user_id,
                                TL.ChannelParticipantCreator   cpc => cpc.user_id,
                                TL.ChannelParticipantBanned    cpb => cpb.peer is TL.PeerUser pu ? pu.user_id : 0,
                                _                                  => 0L
                            };
                            if (userId == 0) continue;

                            bool isAdmin = p is TL.ChannelParticipantAdmin or TL.ChannelParticipantCreator;
                            batch.users.TryGetValue(userId, out var user);
                            result.Add(BuildEntry(userId, user, isAdmin));
                        }

                        offset += batch.participants.Length;
                        onProgress?.Invoke(result.Count, total > 0 ? total : result.Count);

                        if (offset >= batch.count || batch.participants.Length < batchSize)
                            break;

                        // Respect Telegram rate limits
                        await Task.Delay(500, cancellationToken);
                    }
                }
                else
                {
                    // Regular group — members are in Messages_GetFullChat
                    var full = await Client.Messages_GetFullChat(chatDto.Id);
                    if (full?.users is { } usersDict)
                    {
                        foreach (var (uid, u) in usersDict)
                        {
                            if (u is not TL.User user) continue;
                            result.Add(BuildEntry(uid, user, isAdmin: false));
                        }
                        onProgress?.Invoke(result.Count, result.Count);
                    }
                }
            }
            catch (OperationCanceledException) { /* caller cancelled */ }
            catch (Exception ex)
            {
                Log.Warning(ex, "GetChannelMembersAsync failed for chat {ChatName}", chatDto.Name);
            }
            return result;
        }

        private static MemberEntry BuildEntry(long userId, TL.User? user, bool isAdmin) =>
            new()
            {
                UserId    = userId,
                FirstName = user?.first_name ?? string.Empty,
                LastName  = user?.last_name  ?? string.Empty,
                Username  = user?.MainUsername ?? string.Empty,
                Phone     = user?.phone       ?? string.Empty,
                IsBot     = user?.flags.HasFlag(TL.User.Flags.bot) ?? false,
                IsAdmin   = isAdmin,
            };

        /// <summary>
        /// Exports the full message history of a chat to a JSONL file.
        /// Fetches all messages (newest → oldest) and writes them oldest-first.
        /// File: {basePath}/History/{ChatType}/{ChatName}.jsonl
        /// </summary>
        public Task ExportChatHistoryAsync(
            ChatDto chatDto, string basePath, Action<string>? onStatus = null) =>
            TelegramClientApiGate.RunAsync(() => ExportChatHistoryCoreAsync(chatDto, basePath, onStatus));

        private async Task ExportChatHistoryCoreAsync(
            ChatDto chatDto, string basePath, Action<string>? onStatus)
        {
            try
            {
                if (!await EnsureTelegramReadyAsync().ConfigureAwait(false))
                {
                    onStatus?.Invoke("Not connected to Telegram. Log out and sign in again.");
                    return;
                }

                onStatus?.Invoke($"Exporting history for {chatDto.Name}…");

                var peer = await ResolveInputPeerAsync(chatDto).ConfigureAwait(false);
                if (peer == null)
                {
                    onStatus?.Invoke($"Could not resolve peer for {chatDto.Name}. Click Refresh, then try again.");
                    return;
                }

                // Collect all messages oldest-first using the same pagination as SyncHistoryAsync
                var allEntries = new List<HistoryEntry>();
                int offsetId  = 0;
                const int pageSize = 100;

                while (true)
                {
                    var history = await Client.Messages_GetHistory(peer,
                        offset_id: offsetId, limit: pageSize);

                    // Stop only when the API truly returns nothing (end of history).
                    // Checking history.Messages.Length includes MessageService and MessageEmpty
                    // so a page that is all service messages does not terminate the loop early.
                    if (history.Messages.Length == 0) break;

                    var rawMessages = history.Messages.OfType<Message>().ToList();

                    // Resolve user display names when available (only in full messages responses)
                    Dictionary<long, User>? userMap = null;
                    if (history is TL.Messages_Messages mm)    userMap = mm.users;
                    else if (history is TL.Messages_MessagesSlice ms) userMap = ms.users;
                    else if (history is TL.Messages_ChannelMessages mc) userMap = mc.users;

                    foreach (var msg in rawMessages)
                    {
                        string? senderName = null;
                        if (userMap != null && msg.from_id is PeerUser pu && userMap.TryGetValue(pu.user_id, out var u))
                            senderName = u.first_name + (string.IsNullOrEmpty(u.last_name) ? "" : " " + u.last_name);

                        allEntries.Add(ChatHistoryService.CreateEntry(msg, senderName));
                    }

                    onStatus?.Invoke($"Exporting {chatDto.Name}: {allEntries.Count} messages…");

                    // Partial page = beginning of chat. Use min ID across ALL types to avoid gaps.
                    if (history.Messages.Length < pageSize) break;
                    offsetId = history.Messages.Select(m => m.ID).Min();
                }

                // Sort oldest-first before writing
                allEntries.Sort((a, b) => a.Id.CompareTo(b.Id));

                await ChatHistoryService.WriteFullHistoryAsync(
                    chatDto.Type ?? "Other", chatDto.Name, allEntries, basePath);

                onStatus?.Invoke(
                    $"History export complete: {allEntries.Count} messages → History/{chatDto.Type}/{chatDto.Name}.jsonl");
            }
            catch (Exception ex)
            {
                onStatus?.Invoke($"History export failed: {ex.Message}");
                Log.Error(ex, "ExportChatHistoryAsync failed for chat {ChatName}", chatDto.Name);
            }
        }

        /// <summary>Whether the chat's download toggles allow this message's native media.</summary>
        public static bool IsDownloadTypeEnabledForMessage(Message msg, ChatDto chatDto) =>
            IsMessageTypeEnabled(msg, chatDto);

        /// <summary>Short label when the message has native file/photo media (for browse UI).</summary>
        public static string? GetDownloadPreviewLabel(Message msg) =>
            GetPreviewFileName(msg);

        /// <summary>
        /// Whether the user can queue this message from the browse window (ignores Selected / download toggles).
        /// Any non-empty text, native downloadable media, or other media (e.g. link preview) can be queued;
        /// execution still follows normal routing (Photos/Videos/…/plugins); plain text with no URL falls back
        /// to saving a .txt under Messages when no plugin handles it.
        /// </summary>
        public static bool CanSelectMessageForManualBrowse(Message msg, ChatDto _) =>
            GetPreviewFileName(msg) != null
            || !string.IsNullOrWhiteSpace(msg.message)
            || msg.media != null;

        /// <summary>Whether the user can queue this message for manual download from the browse window.</summary>
        public static bool CanSelectMessageForManualDownload(Message msg, ChatDto chat)
        {
            if (GetPreviewFileName(msg) != null && IsMessageTypeEnabled(msg, chat))
                return true;
            if (!string.IsNullOrEmpty(msg.message) &&
                msg.message.Contains("http", StringComparison.OrdinalIgnoreCase))
                return true;
            if (msg.media == null && chat.IgnoreFileByRegex.Count > 0 && !string.IsNullOrEmpty(msg.message) &&
                chat.IgnoreFileByRegex.Any(p =>
                    System.Text.RegularExpressions.Regex.IsMatch(
                        msg.message, p,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                return true;
            return false;
        }

        /// <summary>
        /// Returns true if the message's media type is enabled in the chat's download settings.
        /// Used to skip enqueuing and downloading messages whose type the user has not selected.
        /// </summary>
        private static bool IsMessageTypeEnabled(Message msg, ChatDto chatDto)
        {
            if (msg.media is MessageMediaPhoto)
                return chatDto.Download.Photos;

            if (msg.media is MessageMediaDocument { document: Document doc })
            {
                // Stickers and voice messages are chat artifacts — never queue them for download
                if (doc.attributes?.Any(a => a is DocumentAttributeSticker) == true) return false;
                if (doc.attributes?.Any(a => a is DocumentAttributeAudio audio &&
                        audio.flags.HasFlag(DocumentAttributeAudio.Flags.voice)) == true) return false;

                var mime = doc.mime_type ?? string.Empty;
                if (mime.Contains("image", StringComparison.OrdinalIgnoreCase)) return chatDto.Download.Photos;
                if (mime.Contains("video", StringComparison.OrdinalIgnoreCase)) return chatDto.Download.Videos;
                if (mime.Contains("audio", StringComparison.OrdinalIgnoreCase)) return chatDto.Download.Music;

                var kind = DocumentMediaKindHelper.GetMessageType(doc);
                return kind switch
                {
                    MessageTypes.Photos => chatDto.Download.Photos,
                    MessageTypes.Videos => chatDto.Download.Videos,
                    MessageTypes.Music  => chatDto.Download.Music,
                    _                     => chatDto.Download.Files
                };
            }

            return false;
        }

        /// <summary>
        /// Display filename for queue preview. Returns null if the message has no native downloadable media.
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
            MonitoredChatHelper.FindMonitored(_configParams?.Chats, peerId);

        /// <summary>
        /// Saves the text content of a message that matched a Filter regex pattern to a .txt file.
        /// The file is placed under {basePath}/{chatFolder}/Messages/{yyyyMMdd_HHmmss}_{id}.txt.
        /// Returns IsSuccess=true on success so the caller fires the End Icon and OnSaved notification.
        /// </summary>
        private async Task<ResultExecute> SaveCapturedTextAsync(Message msg, ChatDto chatDto)
        {
            try
            {
                var basePath = _configParams?.PathSaveFile;
                if (string.IsNullOrEmpty(basePath))
                    return new ResultExecute(chatDto.Name) { ErrorMessage = "No download folder configured" };

                var chatFolder = FolderTemplateHelper.Resolve(
                    chatDto.FolderTemplate, chatDto.Type ?? "Other", chatDto.Name)
                    ?? System.IO.Path.Combine(chatDto.Type ?? "Other", chatDto.Name);

                // Absolute template overrides basePath entirely
                var resolvedBase = System.IO.Path.IsPathRooted(chatFolder) ? chatFolder
                    : System.IO.Path.Combine(basePath, chatFolder);
                var folder = System.IO.Path.Combine(resolvedBase, "Messages");
                System.IO.Directory.CreateDirectory(folder);

                var safeName = $"{msg.date:yyyyMMdd_HHmmss}_{msg.ID}.txt";
                var filePath = System.IO.Path.Combine(folder, safeName);

                var content = $"Chat:       {chatDto.Name}\r\n" +
                              $"Date:       {msg.date:yyyy-MM-dd HH:mm:ss}\r\n" +
                              $"Message ID: {msg.ID}\r\n\r\n" +
                              msg.message;

                await System.IO.File.WriteAllTextAsync(filePath, content, System.Text.Encoding.UTF8);

                return new ResultExecute(chatDto.Name)
                {
                    IsSuccess = true,
                    FileName  = safeName,
                    ErrorMessage = string.Empty,
                };
            }
            catch (Exception ex)
            {
                return new ResultExecute(chatDto.Name)
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message,
                };
            }
        }

        private Task ProcessMissedMessagesAsync(long channelId, ChatDto chatDto) =>
            TelegramClientApiGate.RunAsync(() => ProcessMissedMessagesCoreAsync(channelId, chatDto));

        private async Task ProcessMissedMessagesCoreAsync(long channelId, ChatDto chatDto)
        {
            try
            {
                if (!await EnsureTelegramReadyAsync().ConfigureAwait(false))
                    return;

                if (!_accessHashes.TryGetValue(channelId, out var accessHash))
                {
                    await ResolveInputPeerAsync(chatDto).ConfigureAwait(false);
                    if (!_accessHashes.TryGetValue(channelId, out accessHash))
                        return;
                }

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

                    if (history.Messages.Length == 0) break;

                    var rawMessages = history.Messages.OfType<Message>().ToList();

                    // Stop paginating once all messages on this page are at or below the watermark
                    if (rawMessages.All(m => m.ID <= watermark)) break;

                    // Only process media messages that arrived after the watermark
                    var mediaMessages = rawMessages
                        .Where(m => m.media != null && m.ID > watermark)
                        .ToList();

                    var sem = _semaphore; // Capture before lambda — see Client_OnUpdates for explanation
                    var tasks = mediaMessages.Select(msg => Task.Run(async () =>
                    {
                        await sem.WaitAsync();
                        try
                        {
                            // Mark as seen so live updates don't re-download it (atomic max-update)
                            _highWatermark.AddOrUpdate(channelId, msg.ID, (_, prev) => Math.Max(prev, msg.ID));

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
                        finally { sem.Release(); }
                    }));

                    await Task.WhenAll(tasks);

                    // Pagination: base stop condition on total page size (incl. service messages)
                    if (history.Messages.Length < pageSize) break;
                    offsetId = history.Messages.Select(m => m.ID).Min();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ProcessMissedMessagesAsync failed for channel {ChannelId}", channelId);
            }
        }

        private Task ReactToMessage(ChatDto chatDto, UpdatesBase updates, Message message, string reactionIcon) =>
            TelegramClientApiGate.RunAsync(() => ReactToMessageCore(chatDto, updates, message, reactionIcon));

        private async Task ReactToMessageCore(ChatDto chatDto, UpdatesBase updates, Message message, string reactionIcon)
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
        public Task<List<string>> GetChatAvailableReactionsAsync(ChatDto chatDto) =>
            TelegramClientApiGate.RunAsync(() => GetChatAvailableReactionsCoreAsync(chatDto));

        private async Task<List<string>> GetChatAvailableReactionsCoreAsync(ChatDto chatDto)
        {
            var fallback = new List<string> { "👍", "❤️", "🔥", "👌", "💯", "😂", "😮", "🎉" };
            try
            {
                if (!await EnsureTelegramReadyAsync().ConfigureAwait(false))
                    return fallback;
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

        public Task<IList<ChatDto>> GetAllChats() =>
            TelegramClientApiGate.RunAsync(GetAllChatsCoreAsync);

        private async Task<IList<ChatDto>> GetAllChatsCoreAsync()
        {
            if (!await EnsureTelegramReadyAsync().ConfigureAwait(false))
                throw new InvalidOperationException("Not connected to Telegram. Log out and sign in again.");

            var groups = new List<ChatDto>();
            var seenIds = new HashSet<long>();

            // Fetch both the main dialog list (folder 0) and the archive (folder 1)
            // so channels/groups that were archived in Telegram are also visible.
            foreach (int folderId in new[] { 0, 1 })
                await FetchDialogFolder(folderId, groups, seenIds).ConfigureAwait(false);

            // Deduplicate entries that share the same name and type but differ in members count.
            // This handles migrated groups: when a regular group is upgraded to a supergroup,
            // both the old Chat (0 members) and the new Channel (N members) can appear
            // simultaneously even if the old one is supposed to be inactive.
            // Rule: if multiple entries share the same name + type and at least one has members,
            // drop the ones with 0 members.
            return groups
                .GroupBy(c => (Name: c.Name?.Trim().ToLowerInvariant() ?? string.Empty, c.Type))
                .SelectMany(g =>
                {
                    var list = g.ToList();
                    if (list.Count == 1) return list;
                    var withMembers = list.Where(c => c.MembersCount > 0).ToList();
                    return withMembers.Count > 0 ? withMembers : list;
                })
                .ToList();
        }

        /// <summary>
        /// Mutes or unmutes Telegram notifications for the given chat.
        /// Mute sets mute_until to int.MaxValue (indefinite); unmute sets it to 0.
        /// </summary>
        public Task MuteChatAsync(ChatDto chatDto, bool mute) =>
            TelegramClientApiGate.RunAsync(() => MuteChatCoreAsync(chatDto, mute));

        private async Task MuteChatCoreAsync(ChatDto chatDto, bool mute)
        {
            try
            {
                if (!await EnsureTelegramReadyAsync().ConfigureAwait(false))
                    throw new InvalidOperationException("Not connected to Telegram.");

                if (!TryBuildInputPeerFromCache(chatDto, out var peer) || peer == null)
                {
                    await ResolveInputPeerAsync(chatDto).ConfigureAwait(false);
                    if (!TryBuildInputPeerFromCache(chatDto, out peer) || peer == null)
                        throw new InvalidOperationException($"Could not resolve chat {chatDto.Name}.");
                }

                await Client.Account_UpdateNotifySettings(
                    new TL.InputNotifyPeer { peer = peer },
                    new TL.InputPeerNotifySettings
                    {
                        flags      = TL.InputPeerNotifySettings.Flags.has_mute_until,
                        mute_until = mute ? DateTime.UnixEpoch.AddSeconds(int.MaxValue) : DateTime.UnixEpoch
                    });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "MuteChatAsync failed for chat {ChatName}", chatDto.Name);
                throw;
            }
        }

        private async Task FetchDialogFolder(int folderId, List<ChatDto> groups, HashSet<long> seenIds)
        {
            int offsetId = 0;
            DateTime offsetDate = default;
            InputPeer offsetPeer = null!;
            const int pageSize = 100;
            const int maxPages = 50; // up to 5 000 dialogs per folder

            for (int page = 0; page < maxPages; page++)
            {
                var result = await Client.Messages_GetDialogs(
                    offset_date: offsetDate,
                    offset_id: offsetId,
                    offset_peer: offsetPeer,
                    limit: pageSize,
                    hash: 0,
                    folder_id: folderId);

                // Telegram can return Messages_Dialogs (final/only page) OR
                // Messages_DialogsSlice (more pages exist). Extract common fields
                // from whichever type we received so pagination works correctly.
                Dialog[] pageDialogs;
                MessageBase[] pageMessages;
                Dictionary<long, ChatBase> pageChats;
                Dictionary<long, User> pageUsers;
                bool isLastPage;

                if (result is TL.Messages_DialogsSlice slice)
                {
                    pageDialogs  = slice.dialogs.OfType<Dialog>().ToArray();
                    pageMessages = slice.messages;
                    pageChats    = slice.chats;
                    pageUsers    = slice.users;
                    isLastPage   = pageDialogs.Length < pageSize;
                }
                else if (result is TL.Messages_Dialogs full)
                {
                    pageDialogs  = full.dialogs.OfType<Dialog>().ToArray();
                    pageMessages = full.messages;
                    pageChats    = full.chats;
                    pageUsers    = full.users;
                    isLastPage   = true; // non-slice = everything returned at once
                }
                else break; // Messages_DialogsNotModified or unknown

                if (pageDialogs.Length == 0) break;

                foreach (var kv in pageChats)
                {
                    // Skip groups the user has explicitly left or that were migrated to a supergroup
                    if (kv.Value is TL.Chat grpChat && (!grpChat.IsActive || grpChat.migrated_to != null)) continue;
                    if (!seenIds.Add(kv.Value.ID)) continue;

                    if (kv.Value is TL.Channel tlChannel)
                        _accessHashes[tlChannel.ID] = tlChannel.access_hash;
                    else if (kv.Value is TL.Chat tlChat)
                        _accessHashes[tlChat.ID] = 0;

                    groups.Add(new ChatDto
                    {
                        Id           = kv.Value.ID,
                        Name         = kv.Value.Title,
                        Username     = kv.Value.MainUsername,
                        Type         = kv.Value.IsGroup ? "Group" : "Channel",
                        MembersCount = kv.Value is TL.Channel chMc ? chMc.participants_count
                                     : kv.Value is TL.Chat grpMc  ? grpMc.participants_count : 0
                    });
                }

                foreach (var kv in pageUsers)
                {
                    if (kv.Value is not TL.User user || !seenIds.Add(user.ID)) continue;
                    _accessHashes[user.ID] = user.access_hash;
                    groups.Add(new ChatDto
                    {
                        Id       = user.ID,
                        Name     = $"{user.first_name} {user.last_name}".Trim(),
                        Username = user.MainUsername,
                        Type     = "User"
                    });
                }

                if (isLastPage) break;

                // Build the offset for the next page
                var lastDialog = pageDialogs.LastOrDefault();
                if (lastDialog == null) break;

                offsetId = lastDialog.top_message;

                var topMsg = pageMessages.OfType<Message>()
                    .FirstOrDefault(m => m.ID == lastDialog.top_message);
                if (topMsg == null) break;
                offsetDate = topMsg.Date;

                var peerId = lastDialog.peer; // long in WTelegram 4.x
                bool peerResolved = false;
                if (pageChats.TryGetValue(peerId, out var peerChatBase))
                {
                    offsetPeer = peerChatBase is Channel peerChannel
                        ? new InputPeerChannel(peerId, peerChannel.access_hash)
                        : (InputPeer)new InputPeerChat(peerId);
                    peerResolved = true;
                }
                else if (pageUsers.TryGetValue(peerId, out var peerUserBase) && peerUserBase is User peerUser)
                {
                    offsetPeer = new InputPeerUser(peerId, peerUser.access_hash);
                    peerResolved = true;
                }
                if (!peerResolved) break;
            }
        }
    }
}
