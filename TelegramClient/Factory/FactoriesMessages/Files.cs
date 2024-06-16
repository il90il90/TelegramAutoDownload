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

namespace TelegramClient.Factory.Factories
{

    public class Files : BaseMessage
    {
        private readonly Client client;

        public override MessageTypes TypeMessage { get => MessageTypes.Files; set => throw new NotImplementedException(); }

        public Files(Client client, string pathFolderToSaveFiles) : base(client, pathFolderToSaveFiles)
        {
            this.client = client;
        }

        public override async Task<bool> ExecuteAsync(Message message, ChatDto chatDto)
        {
            if (!chatDto.Download.Files) return false; ;

            if (message.media is MessageMediaDocument mediaDocument)
            {
                var document = (Document)mediaDocument.document;

                var fileName = !string.IsNullOrEmpty(document.Filename) ? document.Filename : document.ID.ToString();
                var pathFolderLocation = PathLocationFolder(chatDto, fileName);
                using var stream = File.OpenWrite(pathFolderLocation);
                await client.DownloadFileAsync(document, stream);
            }
            return true;
        }
    }
}
