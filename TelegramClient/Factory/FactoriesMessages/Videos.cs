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

    public class Videos : BaseMessage
    {
        private readonly Client client;
        

        public override string FileExtension => "mp4";

        public override MessageTypes TypeMessage { get => MessageTypes.Videos; set => throw new NotImplementedException(); }

        public Videos(Client client, string pathFolderToSaveFiles) : base(client, pathFolderToSaveFiles)
        {
            this.client = client;
        }

        public override async Task ExecuteAsync(Message message, ChatDto chatDto)
        {
            if (message.media is MessageMediaDocument mediaVideo)
            {
                var document = (Document)mediaVideo.document;

                var fileName = !string.IsNullOrEmpty(document.Filename) ? document.Filename : document.ID.ToString();
                var pathFolderLocation = PathLocationFolder(chatDto, fileName);
                using var stream = File.OpenWrite(pathFolderLocation);
                await client.DownloadFileAsync(document, stream);
            }
        }
    }
}
