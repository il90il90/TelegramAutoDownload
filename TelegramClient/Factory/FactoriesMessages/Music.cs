using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TelegramClient.Factory.Base;
using TelegramClient.Factory.FactoriesMessages.Enum;
using TelegramClient.Models;
using TL;
using WTelegram;

namespace TelegramClient.Factory.FactoriesMessages
{
    internal class Music : BaseMessage
    {
        private readonly Client client;

        public Music(Client client, string pathFolderToSaveFiles) : base(client, pathFolderToSaveFiles)
        {
            this.client = client;
        }

        public override MessageTypes TypeMessage => MessageTypes.Music;

        public override string FileExtension => "";

        public override MessageTypes TypeMessage { get => MessageTypes.Music; set => throw new NotImplementedException(); }

        public override async Task ExecuteAsync(Message message, ChatDto chatDto)
        {
            if (message.media is MessageMediaDocument mediaDocument)
            {
                var document = (Document)mediaDocument.document;

                var fileName = !string.IsNullOrEmpty(document.Filename) ? document.Filename : document.ID.ToString();
                var pathFolderLocation = PathLocationFolder(chatDto, fileName);
                using var stream = File.OpenWrite(pathFolderLocation);
                await client.DownloadFileAsync(document, stream);
            }
        }
    }
}
