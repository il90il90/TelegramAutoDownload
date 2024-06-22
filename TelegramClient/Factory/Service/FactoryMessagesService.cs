using BasePlugins;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        public FactoryMessagesService(Client client, string pathFolderToSaveFiles)
        {
            var messageTextFactoryService = new MessageTextFactoryService(client, pathFolderToSaveFiles);
            messageTypes =
            [
                new Messages(client,pathFolderToSaveFiles,messageTextFactoryService ),
                new Videos(client, pathFolderToSaveFiles),
                new Photos(client, pathFolderToSaveFiles),
                new Files(client, pathFolderToSaveFiles),
                new Music(client, pathFolderToSaveFiles)
            ];
        }

        public async Task<ResultExecute> ExecuteAsync(Update update, ChatDto chatDto)
        {
            if (update is UpdateNewMessage updateNewMessage)
            {
                var message = (Message)updateNewMessage.message;

                var type = GetTypeOfMessage(message);

                var handleMessage = messageTypes.FirstOrDefault(a => a.TypeMessage.Equals(type));
                if (handleMessage == null) return new ResultExecute();
                return await handleMessage.ExecuteAsync(message, chatDto);
            }
            return new ResultExecute();
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
                if (mime_type.Contains("image"))
                {
                    return MessageTypes.Photos;
                }
                else if (mime_type.Contains("video"))
                {
                    return MessageTypes.Videos;
                }
                else if (mime_type.Contains("audio"))
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
