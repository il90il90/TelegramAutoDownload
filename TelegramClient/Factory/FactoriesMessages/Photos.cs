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
    public class Photos : BaseMessage
    {
        public Photos(Client client, string pathFolderToSaveFiles) : base(client, pathFolderToSaveFiles)
        {
        }

        public override string FileExtension => "jpg";

        public override MessageTypes TypeMessage { get => MessageTypes.Photos; set => throw new NotImplementedException(); }

        public override async Task ExecuteAsync(Message message, ChatDto chatDto)
        {
            if (message.media is MessageMediaDocument { document: Document document })
            {
                var filename = document.Filename;
                var folderLocation = PathLocationFolder(chatDto, filename);
                filename ??= $"{document.id}.{document.mime_type[(document.mime_type.IndexOf('/') + 1)..]}";
                using var fileStream = File.Create(folderLocation);
                await Client.DownloadFileAsync(document, fileStream);
            }
            else if (message.media is MessageMediaPhoto { photo: Photo photo })
            {
                var filename = $"{photo.id}.{FileExtension}";
                var folderLocation = PathLocationFolder(chatDto, filename);
                using var fileStream = File.Create(folderLocation);
                var type = await Client.DownloadFileAsync(photo, fileStream);
                fileStream.Close(); 
            }
        }
    }
}
