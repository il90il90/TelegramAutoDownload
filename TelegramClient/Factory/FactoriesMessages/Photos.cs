using BasePlugins;
using System;
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
    public class Photos : BaseMessage
    {
        public Photos(Client client, string pathFolderToSaveFiles) : base(client, pathFolderToSaveFiles)
        {
        }
        public string FileExtension => "jpg";

        public override MessageTypes TypeMessage => MessageTypes.Photos;

        public override async Task<ResultExecute> ExecuteAsync(Message message, ChatDto chatDto)
        {
            if (!chatDto.Download.Photos) return new ResultExecute(chatDto.Name);
            string fileName = "";

            if (message.media is MessageMediaDocument { document: Document document })
            {
                // Assign filename before duplicate check so fileExist return has a valid name
                fileName = !string.IsNullOrEmpty(document.Filename)
                    ? document.Filename
                    : $"{document.id}.{document.mime_type.Split('/').Last()}";
                var fileExist = GetPathOfDuplicateFile(fileName);
                if (fileExist != null)
                {
                    return new ResultExecute(chatDto.Name)
                    {
                        IsSuccess = true,
                        ErrorMessage = $"{fileName} is exist on {fileExist}"
                    };
                }
                var folderLocation = PathLocationFolder(chatDto, fileName);
                using var fileStream = File.Create(folderLocation);
                await Client.DownloadFileAsync(document, fileStream);
            }
            else if (message.media is MessageMediaPhoto { photo: Photo photo })
            {
                fileName = $"{photo.id}.{FileExtension}";
                var fileExist = GetPathOfDuplicateFile(fileName);
                if (fileExist != null)
                {
                    return new ResultExecute(chatDto.Name)
                    {
                        IsSuccess = true,
                        ErrorMessage = $"{fileName} is exist on {fileExist}"
                    };
                }
                var folderLocation = PathLocationFolder(chatDto, fileName);
                using var fileStream = File.Create(folderLocation);
                await Client.DownloadFileAsync(photo, fileStream);
            }
            return new ResultExecute(chatDto.Name)
            {
                IsSuccess = true,
                FileName = fileName,
            };
        }
    }
}
