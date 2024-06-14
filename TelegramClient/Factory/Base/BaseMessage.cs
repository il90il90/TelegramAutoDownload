using System.IO;
using System.Threading.Tasks;
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
        public abstract MessageTypes TypeMessage { get; set; }

        public abstract string FileExtension { get; }

        public BaseMessage(Client client, string pathFolderToSaveFiles)
        {
            Client = client;
            PathFolderToSaveFiles = pathFolderToSaveFiles;
        }
        public abstract Task ExecuteAsync(Message message, ChatDto chatDto);

        public string PathLocationFolder(ChatDto chatDto, string fileName)
        {
            var path = CreateFolderIfNotExist(chatDto);
            return Path.Combine($"{path}/{chatDto.Name}", $"{fileName}.{FileExtension}");
        }

        private string CreateFolderIfNotExist(ChatDto chatDto)
        {
            var fullPathOfFolder = PathFolderToSaveFiles == null ? $"{TypeMessage}" : $"{PathFolderToSaveFiles}/{TypeMessage}";
            if (!Directory.Exists(fullPathOfFolder))
            {
                Directory.CreateDirectory(fullPathOfFolder);
            }

            var fullPath = $"{fullPathOfFolder}/{chatDto.Name}";
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }

            return fullPathOfFolder;
        }
    }
}
