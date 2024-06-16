using System.Collections.Generic;
using System.Threading.Tasks;
using TelegramClient.Factory.Base;
using TelegramClient.Factory.Factories;
using TelegramClient.Factory.FactoriesMessages.Enum;
using TelegramClient.Factory.Interfaces.Messages;
using TelegramClient.Models;
using TL;
using WTelegram;

namespace TelegramClient.Factory.Service
{
    public class MessageTextFactoryService : BaseMessage
    {
        readonly List<IMessageType> messageTexts;

        public override MessageTypes TypeMessage { get; set; }

        public MessageTextFactoryService(Client client, string pathFolderToSaveFiles) : base(client, pathFolderToSaveFiles)
        {
            messageTexts = [
                new YouTube(client, pathFolderToSaveFiles)
            ];
        }

        public override async Task<bool> ExecuteAsync(Message message, ChatDto chatDto)
        {
            var resultExecute = false;
            foreach (var item in messageTexts)
            {
                var split = message.message.Split('\n');
                foreach (var line in split)
                {
                    TypeMessage = item.TypeMessage;
                    resultExecute = await item.ExecuteAsync(new Message()
                    {
                        message = line
                    }, chatDto);
                }
            }
            return resultExecute;
        }
    }
}
