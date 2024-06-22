using BasePlugins;
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

        public override MessageTypes TypeMessage => MessageTypes.Message;

        public override async Task<ResultExecute> ExecuteAsync(TL.Message message, ChatDto chatDto)
        {
            return await _messageTextFactory.ExecuteAsync(message, chatDto);
        }
    }
}
