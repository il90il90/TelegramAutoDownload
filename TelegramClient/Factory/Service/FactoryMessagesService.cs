using BasePlugins;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TelegramClient.Factory.Base;
using TelegramClient.Factory.Factories;
using TelegramClient.Factory.FactoriesMessages;
using TelegramClient.Factory.FactoriesMessages.Enum;
using TelegramClient.Factory.Interfaces.Messages;
using TelegramClient.Models;
using TL;
using WTelegram;

namespace TelegramClient.Factory.Service
{
    public class FactoryMessagesService
    {
        private readonly List<IMessageType> messageTypes;
        private readonly MessageTextFactoryService _messageTextFactory;

        /// <summary>
        /// (chatName, fileName, pluginName, percent, bytesDownloaded, totalBytes)
        /// </summary>
        public Action<string, string, string, double, long, long>? OnProgress { get; set; }

        /// <summary>
        /// (chatName, fileName, success)
        /// </summary>
        public Action<string, string, bool>? OnComplete { get; set; }

        /// <summary>
        /// Called when FILE_REFERENCE_EXPIRED is encountered.
        /// Should return a fresh copy of the message, or null if unavailable.
        /// (chatDto, messageId) → fresh Message
        /// </summary>
        public Func<ChatDto, int, Task<Message?>>? RefreshMessage { get; set; }

        public FactoryMessagesService(Client client, string pathFolderToSaveFiles)
        {
            _messageTextFactory = new MessageTextFactoryService(client, pathFolderToSaveFiles);
            messageTypes =
            [
                new Messages(client, pathFolderToSaveFiles, _messageTextFactory),
                new Videos(client, pathFolderToSaveFiles),
                new Photos(client, pathFolderToSaveFiles),
                new Files(client, pathFolderToSaveFiles),
                new Music(client, pathFolderToSaveFiles)
            ];
        }

        /// <summary>
        /// Distributes progress callbacks to all registered message handlers,
        /// including the text/plugin handler (MessageTextFactoryService).
        /// Must be called after constructing if you want progress reporting.
        /// </summary>
        public void WireProgressCallbacks()
        {
            foreach (var mt in messageTypes.OfType<BaseMessage>())
            {
                mt.OnProgress = OnProgress;
                mt.OnComplete = OnComplete;
            }

            // MessageTextFactoryService is not in messageTypes (it's passed as a dependency),
            // so wire it explicitly so plugins loaded inside it receive the callbacks.
            _messageTextFactory.OnProgress = OnProgress;
            _messageTextFactory.OnComplete = OnComplete;
        }

        public async Task<ResultExecute> ExecuteAsync(Update update, ChatDto chatDto)
        {
            if (update is UpdateNewMessage updateNewMessage)
            {
                if (updateNewMessage.message is Message message)
                    return await ExecuteDirectAsync(message, chatDto);
            }
            return new ResultExecute(chatDto.Name);
        }

        /// <summary>
        /// Processes a Message directly — used for historical/catch-up messages
        /// that don't arrive as UpdateNewMessage.
        /// Automatically retries once with a fresh message when FILE_REFERENCE_EXPIRED is detected.
        /// </summary>
        public async Task<ResultExecute> ExecuteDirectAsync(Message message, ChatDto chatDto)
        {
            var result = await RunHandlerAsync(message, chatDto);

            // Telegram file references expire after some time.
            // Re-fetch the message to get a new reference and retry once.
            if (result.ErrorMessage?.Contains("FILE_REFERENCE_EXPIRED") == true && RefreshMessage != null)
            {
                Log.Information(
                    "FILE_REFERENCE_EXPIRED — fetching fresh message and retrying once: utc={Utc:o} chat={Chat} msgId={MsgId}",
                    DateTime.UtcNow, chatDto.Name, message.ID);
                var freshMessage = await RefreshMessage(chatDto, message.ID);
                if (freshMessage != null)
                    result = await RunHandlerAsync(freshMessage, chatDto);
            }

            return result;
        }

        private async Task<ResultExecute> RunHandlerAsync(Message message, ChatDto chatDto)
        {
            var type = GetTypeOfMessage(message);
            var kindLabel = type.ToString();
            var handleMessage = messageTypes.FirstOrDefault(a => a.TypeMessage.Equals(type));
            if (handleMessage == null)
            {
                Log.Debug("No download handler for chat={Chat} msgId={MsgId} inferredKind={Kind}",
                    chatDto.Name, message.ID, kindLabel);
                return new ResultExecute(chatDto.Name);
            }

            var downloadPolicyResult = handleMessage.CheckDownloadPolicy(chatDto, message);
            if (!downloadPolicyResult.IsSuccess)
            {
                Log.Information(
                    "Download blocked by policy: utc={Utc:o} chat={Chat} msgId={MsgId} kind={Kind} reason={Reason}",
                    DateTime.UtcNow, chatDto.Name, message.ID, kindLabel, downloadPolicyResult.ErrorMessage ?? "");
                return downloadPolicyResult;
            }

            var logLifecycle = ExpectsDownloadLogLifecycle(message, type, chatDto);
            if (logLifecycle)
            {
                Log.Information(
                    "Download started: utc={Utc:o} chat={Chat} msgId={MsgId} kind={Kind}",
                    DateTime.UtcNow, chatDto.Name, message.ID, kindLabel);
            }

            var sw = Stopwatch.StartNew();
            ResultExecute resultExecute;
            try
            {
                resultExecute = await handleMessage.ExecuteAsync(message, chatDto);
            }
            catch (Exception ex)
            {
                sw.Stop();
                Log.Error(ex,
                    "Download handler threw: utc={Utc:o} chat={Chat} msgId={MsgId} kind={Kind} elapsedMs={Ms}",
                    DateTime.UtcNow, chatDto.Name, message.ID, kindLabel, sw.ElapsedMilliseconds);
                throw;
            }

            sw.Stop();
            resultExecute.MessageType = type.ToString();
            LogDownloadOutcome(message, chatDto, kindLabel, resultExecute, sw.ElapsedMilliseconds, logLifecycle);
            return resultExecute;
        }

        /// <summary>
        /// Plain text with no URL — not a failure; the message pipeline ran but had nothing to do.
        /// </summary>
        public static bool IsBenignNoWorkOutcome(ResultExecute r) =>
            !r.IsSuccess &&
            (r.ErrorMessage?.StartsWith("No http/https URL", StringComparison.OrdinalIgnoreCase) ?? false);

        /// <summary>User pressed Cancel in the UI — intentional, not an error.</summary>
        public static bool IsUserCancelledOutcome(ResultExecute r) =>
            !r.IsSuccess &&
            string.Equals(r.ErrorMessage, "Cancelled by user", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when the message may perform network I/O (media, URL plugins, or filter text capture).
        /// Used to avoid spamming logs for plain text that no plugin handles.
        /// </summary>
        private static bool ExpectsDownloadLogLifecycle(Message message, MessageTypes type, ChatDto chatDto)
        {
            if (type != MessageTypes.Message) return true;
            if (message.media != null) return true;
            var text = message.message ?? string.Empty;
            if (text.Contains("http", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.Contains("magnet:", StringComparison.OrdinalIgnoreCase)) return true;
            if (FilterPatternHelper.ShouldCaptureText(text, FilterPatternHelper.GetPatterns(chatDto).ToList()))
                return true;
            return false;
        }

        private static void LogDownloadOutcome(
            Message message, ChatDto chatDto, string kindLabel,
            ResultExecute r, long elapsedMs, bool logLifecycle)
        {
            var interesting = logLifecycle
                || (r.IsSuccess && (!string.IsNullOrEmpty(r.FileName) || !string.IsNullOrEmpty(r.FilePath)))
                || (r.IsSuccess && !string.IsNullOrEmpty(r.ErrorMessage))
                || (!r.IsSuccess && !IsBenignNoWorkOutcome(r));

            if (!interesting)
            {
                Log.Debug("Message pipeline finished with no download work: chat={Chat} msgId={MsgId}",
                    chatDto.Name, message.ID);
                return;
            }

            if (r.IsSuccess && !string.IsNullOrEmpty(r.ErrorMessage))
            {
                Log.Information(
                    "Download skipped after check: utc={Utc:o} chat={Chat} msgId={MsgId} kind={Kind} elapsedMs={Ms} detail={Detail}",
                    DateTime.UtcNow, chatDto.Name, message.ID, kindLabel, elapsedMs, r.ErrorMessage);
                return;
            }

            if (r.IsSuccess)
            {
                Log.Information(
                    "Download completed: utc={Utc:o} chat={Chat} msgId={MsgId} kind={Kind} file={File} path={Path} elapsedMs={Ms}",
                    DateTime.UtcNow, chatDto.Name, message.ID, kindLabel,
                    string.IsNullOrEmpty(r.FileName) ? "—" : r.FileName,
                    string.IsNullOrEmpty(r.FilePath) ? "—" : r.FilePath,
                    elapsedMs);
                return;
            }

            if (IsUserCancelledOutcome(r))
            {
                Log.Information(
                    "Download cancelled by user: utc={Utc:o} chat={Chat} msgId={MsgId} kind={Kind} file={File} elapsedMs={Ms}",
                    DateTime.UtcNow, chatDto.Name, message.ID, kindLabel,
                    string.IsNullOrEmpty(r.FileName) ? "—" : r.FileName,
                    elapsedMs);
                return;
            }

            var err = string.IsNullOrWhiteSpace(r.ErrorMessage) ? "(no error text supplied)" : r.ErrorMessage;
            Log.Warning(
                "Download failed: utc={Utc:o} chat={Chat} msgId={MsgId} kind={Kind} file={File} error={Error} elapsedMs={Ms}",
                DateTime.UtcNow, chatDto.Name, message.ID, kindLabel,
                string.IsNullOrEmpty(r.FileName) ? "—" : r.FileName,
                err,
                elapsedMs);
        }

        public MessageTypes GetTypeOfMessage(Message message)
        {
            if (message.media is MessageMediaPhoto)
            {
                return MessageTypes.Photos;
            }
            else if (message.media is MessageMediaDocument document)
            {
                var doc = (Document)document.document;
                return DocumentMediaKindHelper.GetMessageType(doc);
            }
            else
            {
                return MessageTypes.Message;
            }
        }
    }
}
