using BasePlugins;
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

        /// <summary>
        /// Called periodically during download: (chatName, fileName, pluginName, percent, bytesDownloaded, totalBytes)
        /// </summary>
        public Action<string, string, string, double, long, long>? OnProgress { get; set; }

        /// <summary>
        /// Called when download finishes: (chatName, fileName, success)
        /// </summary>
        public Action<string, string, bool>? OnComplete { get; set; }

        public BaseMessage(Client client, string pathFolderToSaveFiles)
        {
            Client = client;
            PathFolderToSaveFiles = pathFolderToSaveFiles;
        }
        public abstract Task<ResultExecute> ExecuteAsync(Message message, ChatDto chatDto);

        /// <summary>
        /// Creates a WTelegram ProgressCallback that reports download bytes to OnProgress.
        /// Also checks the CancellationRegistry on each chunk so the UI can cancel mid-stream.
        /// </summary>
        protected Client.ProgressCallback? MakeProgress(string chatName, string fileName, long totalBytes)
        {
            if (OnProgress == null) return null;
            var cancelKey = CancellationRegistry.MakeKey(chatName, fileName);
            var cts = new System.Threading.CancellationTokenSource();
            // Store in registry so the UI cancel button can trigger it
            CancellationRegistry.Register(cancelKey);
            return (transmitted, total) =>
            {
                long effectiveTotal = total > 0 ? total : totalBytes;
                double pct = effectiveTotal > 0 ? transmitted * 100.0 / effectiveTotal : 0;
                OnProgress.Invoke(chatName, fileName, TypeMessage.ToString(), Math.Min(99, pct), transmitted, effectiveTotal);
            };
        }

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

                // Skip download if the file is smaller than the configured minimum threshold
                if (chatDto.DownloadFromSize != 0 && documentSizeInMb < chatDto.DownloadFromSize)
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
