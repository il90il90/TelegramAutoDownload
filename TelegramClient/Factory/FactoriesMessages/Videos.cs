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

    public class Videos : BaseMessage
    {
        public override MessageTypes TypeMessage => MessageTypes.Videos;
        private string PluginName => "Videos";

        public Videos(Client client, string pathFolderToSaveFiles) : base(client, pathFolderToSaveFiles)
        {
        }

        public override async Task<ResultExecute> ExecuteAsync(Message message, ChatDto chatDto)
        {
            if (!chatDto.Download.Videos) return new ResultExecute(chatDto.Name);
            if (message.media is not MessageMediaDocument mediaVideo) return new ResultExecute(chatDto.Name);

            var document = (Document)mediaVideo.document;
            var mime_type = "mp4";
            string fileName;

            if (!string.IsNullOrEmpty(document.Filename))
            {
                mime_type = document.Filename.Split('.').LastOrDefault();
                fileName = document.Filename;
            }
            else
            {
                fileName = $"{document.ID}.{mime_type}";
            }

            var fileExist = GetPathOfDuplicateFile(fileName);
            if (fileExist != null)
                return new ResultExecute(chatDto.Name) { IsSuccess = true, ErrorMessage = $"{fileName} is exist on {fileExist}" };

            var pathFolderLocation = PathLocationFolder(chatDto, fileName);
            OnProgress?.Invoke(chatDto.Name, fileName, PluginName, 0, 0, document.size);
            try
            {
                await WithRetryAsync(async () =>
                {
                    using var stream = File.Create(pathFolderLocation);
                    await Client.DownloadFileAsync(document, stream, null, MakeProgress(chatDto.Name, fileName, document.size));
                    return true;
                });
                OnComplete?.Invoke(chatDto.Name, fileName, true);
                return new ResultExecute(chatDto.Name) { IsSuccess = true, FileName = fileName, FilePath = pathFolderLocation };
            }
            catch (OperationCanceledException)
            {
                DeletePartialFile(pathFolderLocation);
                return new ResultExecute(chatDto.Name) { IsSuccess = false, FileName = fileName, ErrorMessage = "Cancelled by user" };
            }
            catch (Exception e)
            {
                OnComplete?.Invoke(chatDto.Name, fileName, false);
                return new ResultExecute(chatDto.Name) { IsSuccess = false, FileName = fileName, ErrorMessage = e.Message };
            }
        }

    }

}
