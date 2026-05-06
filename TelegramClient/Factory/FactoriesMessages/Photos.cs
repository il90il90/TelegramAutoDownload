using BasePlugins;
using System;
using System.IO;
using System.Linq;
using System.Threading;
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
            string savedPath = "";

            if (message.media is MessageMediaDocument { document: Document document })
            {
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
                savedPath = PathLocationFolder(chatDto, fileName);
                OnProgress?.Invoke(chatDto.Name, fileName, TypeMessage.ToString(), 0, 0, document.size);
                var (progress, downloadToken) = MakeProgress(chatDto.Name, fileName, document.size);
                try
                {
                    await WithRetryAsync(async () =>
                    {
                        using var fileStream = File.Create(savedPath);
                        // Dispose the stream on cancel so a hung DownloadFileAsync is force-interrupted
                        using var _ = downloadToken.Register(() => { try { fileStream.Dispose(); } catch { } });
                        await Client.DownloadFileAsync(document, fileStream, null, progress);
                        return true;
                    }, downloadToken);
                    OnComplete?.Invoke(chatDto.Name, fileName, true);
                }
                catch (OperationCanceledException)
                {
                    DeletePartialFile(savedPath);
                    return new ResultExecute(chatDto.Name) { IsSuccess = false, FileName = fileName, ErrorMessage = "Cancelled by user" };
                }
                catch (Exception) when (downloadToken.IsCancellationRequested)
                {
                    DeletePartialFile(savedPath);
                    return new ResultExecute(chatDto.Name) { IsSuccess = false, FileName = fileName, ErrorMessage = "Download cancelled (no progress)" };
                }
                catch (Exception ex)
                {
                    OnComplete?.Invoke(chatDto.Name, fileName, false);
                    return new ResultExecute(chatDto.Name) { IsSuccess = false, FileName = fileName, ErrorMessage = ex.Message };
                }
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
                savedPath = PathLocationFolder(chatDto, fileName);
                OnProgress?.Invoke(chatDto.Name, fileName, TypeMessage.ToString(), 0, 0, 0);
                // Photos are small but apply a 5-minute timeout to guard against hung connections
                using var photoCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                using var fileStream = File.Create(savedPath);
                using var _ = photoCts.Token.Register(() => { try { fileStream.Dispose(); } catch { } });
                await Client.DownloadFileAsync(photo, fileStream);
                OnComplete?.Invoke(chatDto.Name, fileName, true);
            }
            return new ResultExecute(chatDto.Name)
            {
                IsSuccess = true,
                FileName = fileName,
                FilePath = savedPath
            };
        }
    }
}
