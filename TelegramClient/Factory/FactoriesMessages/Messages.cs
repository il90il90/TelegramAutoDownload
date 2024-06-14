using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramClient.Factory.Base;
using TelegramClient.Factory.FactoriesMessages.Enum;
using TelegramClient.Factory.Service;
using TelegramClient.Models;
using WTelegram;

namespace TelegramClient.Factory.Factories
{
    public class Messages : BaseMessage
    {
        private MessageTextFactoryService _messageTextFactory;

        public Messages(Client client, string pathFolderToSaveFiles, MessageTextFactoryService messageTextFactory) : base(client, pathFolderToSaveFiles)
        {
            _messageTextFactory = messageTextFactory;
        }

        public override string FileExtension => null;

        public override MessageTypes TypeMessage { get => MessageTypes.Message; set => throw new NotImplementedException(); }

        public override async Task ExecuteAsync(TL.Message message, ChatDto chatDto)
        {
            await _messageTextFactory.ExecuteAsync(message, chatDto);
        }
    }
}
