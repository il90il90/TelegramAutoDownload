using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TelegramClient.Factory.Base;
using TelegramClient.Factory.FactoriesMessages.Enum;
using TelegramClient.Models;
using WTelegram;

namespace TelegramClient.Factory.Factories
{
    public class Messages : BaseMessage
    {

        public Messages(Client client, string pathFolderToSaveFiles) : base(client, pathFolderToSaveFiles)
        {
        }

        public override MessageTypes TypeMessage => MessageTypes.Message;

        public override string FileExtension => null;

        public override Task ExecuteAsync(TL.Message message, ChatDto chatDto)
        {
            //if (message.message.StartsWith("https://youtu"))
            //{
            //}
            return Task.CompletedTask;
        }
    }
}
