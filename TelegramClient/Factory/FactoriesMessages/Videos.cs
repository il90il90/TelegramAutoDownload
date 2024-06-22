﻿using BasePlugins;
using System.IO;
using System.Linq;
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

        public override async Task<ResultExecute> ExecuteAsync(Message message, ChatDto chatDto)
        {
            if (!chatDto.Download.Videos) return new ResultExecute(false);

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
                
                return new ResultExecute(true)
                {
                    Name = fileName
                };
            }
            return new ResultExecute(false);
        }
    }
}
