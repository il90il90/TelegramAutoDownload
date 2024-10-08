﻿using BasePlugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
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
                fileName = document.Filename;
                var fileExist = GetPathOfDuplicateFile(fileName);
                if (fileExist != null)
                {
                    return new ResultExecute(chatDto.Name)
                    {
                        IsSuccess = true,
                        ErrorMessage = $"{fileName} is exist on {fileExist}"
                    };
                }
                fileName ??= $"{document.id}.{document.mime_type[(document.mime_type.IndexOf('/') + 1)..]}";
                var folderLocation = PathLocationFolder(chatDto, fileName);
                using var fileStream = File.Create(folderLocation);
                await Client.DownloadFileAsync(document, fileStream);
                fileStream.Close();
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
                var type = await Client.DownloadFileAsync(photo, fileStream);
                fileStream.Close();
            }
            return new ResultExecute(chatDto.Name)
            {
                IsSuccess = true,
                FileName = fileName,
            };
        }
    }
}
