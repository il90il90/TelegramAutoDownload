using BasePlugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TelegramClient.Factory.Base;
using TelegramClient.Factory.Factories;
using TelegramClient.Factory.FactoriesMessages;
using TelegramClient.Factory.FactoriesMessages.Enum;
using TelegramClient.Factory.Interfaces.Messages;
using TelegramClient.Models;
using TL;
using TL.Methods;
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
        /// </summary>
        public async Task<ResultExecute> ExecuteDirectAsync(Message message, ChatDto chatDto)
        {
            var type = GetTypeOfMessage(message);
            var handleMessage = messageTypes.FirstOrDefault(a => a.TypeMessage.Equals(type));
            if (handleMessage == null) return new ResultExecute(chatDto.Name);

            var downloadPolicyResult = handleMessage.CheckDownloadPolicy(chatDto, message);
            if (downloadPolicyResult.IsSuccess)
            {
                var resultExecute = await handleMessage.ExecuteAsync(message, chatDto);
                resultExecute.MessageType = type.ToString();
                return resultExecute;
            }

            return downloadPolicyResult;
        }

        public MessageTypes GetTypeOfMessage(Message message)
        {
            if (message.media is MessageMediaPhoto)
            {
                return MessageTypes.Photos;
            }
            else if (message.media is MessageMediaDocument document)
            {
                var mime_type = ((Document)document.document).mime_type;
                if (mime_type?.Contains("image") == true)
                {
                    return MessageTypes.Photos;
                }
                else if (mime_type?.Contains("video") == true)
                {
                    return MessageTypes.Videos;
                }
                else if (mime_type?.Contains("audio") == true)
                {
                    return MessageTypes.Music;
                }
                else
                {
                    return MessageTypes.Files;
                }
            }
            else
            {
                return MessageTypes.Message;
            }
        }
    }
}
