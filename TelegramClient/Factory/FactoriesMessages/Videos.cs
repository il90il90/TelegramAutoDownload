using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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



        public override MessageTypes TypeMessage => MessageTypes.Videos;

        public Videos(Client client, string pathFolderToSaveFiles) : base(client, pathFolderToSaveFiles)
        {
            this.client = client;
        }

        public override async Task<bool> ExecuteAsync(Message message, ChatDto chatDto)
        {
            if (!chatDto.Download.Videos) return false;

            if (message.media is MessageMediaDocument mediaVideo)
            {
                var document = (Document)mediaVideo.document;
                string fileName;
                var mime_type = "mp4";
                if (!string.IsNullOrEmpty(document.Filename))
                {
                    mime_type = document.Filename.Split('.').LastOrDefault();
                    fileName = document.Filename;
                }
                else
                {
                    fileName = $"{document.ID}.{mime_type}";
                }
                var pathFolderLocation = PathLocationFolder(chatDto, fileName);
                using var stream = File.OpenWrite(pathFolderLocation);
                await client.DownloadFileAsync(document, stream);
            }
            return true;
        }
    }
}
