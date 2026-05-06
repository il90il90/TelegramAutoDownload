using BasePlugins;
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

    public class Files : BaseMessage
    {
        public override MessageTypes TypeMessage => MessageTypes.Files;

        public Files(Client client, string pathFolderToSaveFiles) : base(client, pathFolderToSaveFiles)
        {
        }

        public override async Task<ResultExecute> ExecuteAsync(Message message, ChatDto chatDto)
        {
            if (!chatDto.Download.Files) return new ResultExecute(chatDto.Name);

            if (message.media is MessageMediaDocument mediaDocument)
            {
                var document = (Document)mediaDocument.document;
                var fileName = !string.IsNullOrEmpty(document.Filename) ? document.Filename : document.ID.ToString();

                var fileExist = GetPathOfDuplicateFile(fileName);
                if (fileExist != null)
                {
                    return new ResultExecute(chatDto.Name)
                    {
                        IsSuccess = true,
                        ErrorMessage = $"{fileName} is exist on {fileExist}"
                    };
                }

                var pathFolderLocation = PathLocationFolder(chatDto, fileName);
                OnProgress?.Invoke(chatDto.Name, fileName, TypeMessage.ToString(), 0, 0, document.size);
                var (progress, downloadToken) = MakeProgress(chatDto.Name, fileName, document.size);
                try
                {
                    await WithRetryAsync(async () =>
                    {
                        using var stream = File.Create(pathFolderLocation);
                        // Dispose the stream on cancel so a hung DownloadFileAsync is force-interrupted
                        using var _ = downloadToken.Register(() => { try { stream.Dispose(); } catch { } });
                        await Client.DownloadFileAsync(document, stream, null, progress);
                        return true;
                    }, downloadToken);
                    OnComplete?.Invoke(chatDto.Name, fileName, true);
                    return new ResultExecute(chatDto.Name) { IsSuccess = true, FileName = fileName, FilePath = pathFolderLocation };
                }
                catch (OperationCanceledException)
                {
                    DeletePartialFile(pathFolderLocation);
                    return new ResultExecute(chatDto.Name) { IsSuccess = false, FileName = fileName, ErrorMessage = "Cancelled by user" };
                }
                catch (Exception) when (downloadToken.IsCancellationRequested)
                {
                    DeletePartialFile(pathFolderLocation);
                    return new ResultExecute(chatDto.Name) { IsSuccess = false, FileName = fileName, ErrorMessage = "Download cancelled (no progress)" };
                }
                catch (Exception ex)
                {
                    OnComplete?.Invoke(chatDto.Name, fileName, false);
                    return new ResultExecute(chatDto.Name) { IsSuccess = false, FileName = fileName, ErrorMessage = ex.Message };
                }
            }
            return new ResultExecute(chatDto.Name);
        }
    }
}
