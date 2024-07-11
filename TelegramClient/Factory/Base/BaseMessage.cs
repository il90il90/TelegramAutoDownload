using BasePlugins;
using System.IO;
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

                if (chatDto.Size != 0 && chatDto.Size <= documentSizeInMb)
                {
                    return new ResultExecute(chatDto.Name)
                    {
                        FileName = document.Filename,
                        IsSuccess = false,
                        ErrorMessage = $"file limit to start download is: {chatDto.Size}MB, and the original file is: {documentSizeInMb}MB",
                    };
                }

                foreach (var regexPattern in chatDto.Regex)
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

        protected string[] FileExistDuplicate(string fileName)
        {
            return Directory.GetFiles(PathFolderToSaveFiles, fileName, SearchOption.AllDirectories);

        }


    }
}
