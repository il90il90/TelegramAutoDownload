﻿using BasePlugins;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TelegramClient.Factory.Factories;
using TelegramClient.Factory.FactoriesMessages.Enum;
using TelegramClient.Factory.Interfaces.Messages;
using TelegramClient.Models;
using TL;
using WTelegram;

namespace TelegramClient.Factory.Base
{
    public abstract class BaseMessage : IMessageType
    {
        public Client Client { get; }
        public string PathFolderToSaveFiles { get; }
        public abstract MessageTypes TypeMessage { get; }

        public BaseMessage(Client client, string pathFolderToSaveFiles)
        {
            Client = client;
            PathFolderToSaveFiles = pathFolderToSaveFiles;
        }
        public abstract Task<ResultExecute> ExecuteAsync(Message message, ChatDto chatDto);

        public string PathLocationFolder(ChatDto chatDto, string fileName)
        {
            var folderName = chatDto.Name.TrimEnd();
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                fileName = fileName.Replace(c, ' ');
                folderName = folderName.Replace(c, ' ');
            }

            return CreateFolderIfNotExist(folderName, fileName);
        }

        public ResultExecute CheckDownloadPolicy(ChatDto chatDto, Message message)
        {
            if (message.media is MessageMediaDocument media && media.document is Document document)
            {
                var documentSizeInMb = document.size / 1024 / 1024;

                if (chatDto.DownloadFromSize != 0 && chatDto.DownloadFromSize <= documentSizeInMb)
                {
                    return new ResultExecute(chatDto.Name)
                    {
                        FileName = document.Filename,
                        IsSuccess = false,
                        ErrorMessage = $"file limit to start download is: {chatDto.DownloadFromSize}MB, and the original file is: {documentSizeInMb}MB",
                    };
                }

                foreach (var regexPattern in chatDto.IgnoreFileByRegex)
                {
                    Regex regex = new(regexPattern);
                    if (regex.IsMatch(document.Filename))
                    {
                        return new ResultExecute(chatDto.Name)
                        {
                            FileName = document.Filename,
                            IsSuccess = false,
                            ErrorMessage = $"skip by regex pattern: '{regexPattern}' matched the document filename: {document.Filename}"
                        };
                    }
                }
                return new ResultExecute(chatDto.Name)
                {
                    IsSuccess = true
                };
            }
            else
            {
                //ignore policy for plugins 
                return new ResultExecute(chatDto.Name)
                {
                    IsSuccess = true
                };
            }
        }

        private string CreateFolderIfNotExist(string folderName, string fileName)
        {
            var fullPathOfFolder = PathFolderToSaveFiles == null ? $"{TypeMessage}" : $"{PathFolderToSaveFiles}/{TypeMessage}";
            if (!Directory.Exists(fullPathOfFolder))
            {
                Directory.CreateDirectory(fullPathOfFolder);
            }

            var fullPath = $"{fullPathOfFolder}/{folderName}";
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }

            return Path.Combine($"{fullPath}", $"{fileName}");
        }

        protected string GetPathOfDuplicateFile(string fileName)
        {
            try
            {
                var rootPathByType = $"{PathFolderToSaveFiles}/{TypeMessage}";

                var folders = Directory.GetDirectories(rootPathByType);
                foreach (var folder in folders)
                {
                    var nameFolder = folder.Split("\\").LastOrDefault();
                    var files = Directory.GetFiles(folder);
                    foreach (var file in files)
                    {
                        var nameFile = file.Split("\\").LastOrDefault();
                        if (nameFile == fileName)
                        {
                            return $"{nameFolder}";
                        }
                    }
                }

                return null;

            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
