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

                // Primary dedup: Telegram document ID (unique content fingerprint)
                if (FileDownloadIndex.IsAlreadyDownloaded(document.ID))
                {
                    // Verify the file still exists on disk — guards against stale index after reinstall / moved files
                    var existingFile = GetPathOfDuplicateFile(fileName, document.size);
                    if (existingFile != null)
                        return new ResultExecute(chatDto.Name) { IsSuccess = true, FileName = fileName, ErrorMessage = $"{fileName} already downloaded (id match)" };
                    // Stale index entry — file gone from disk, remove and re-download
                    FileDownloadIndex.Remove(document.ID);
                }

                // Secondary dedup: filename + file size match on disk
                var fileExist = GetPathOfDuplicateFile(fileName, document.size);
                if (fileExist != null)
                {
                    FileDownloadIndex.MarkDownloaded(document.ID);
                    return new ResultExecute(chatDto.Name) { IsSuccess = true, FileName = fileName, ErrorMessage = $"{fileName} is exist on {fileExist}" };
                }

                var pathFolderLocation = PathLocationFolder(chatDto, fileName);
                var partPath = GetPartFilePath(pathFolderLocation);
                OnProgress?.Invoke(chatDto.Name, fileName, TypeMessage.ToString(), 0, 0, document.size);
                var (progress, downloadToken, userCancelToken) = MakeProgress(chatDto.Name, fileName, document.size);
                try
                {
                    await WithRetryAsync(async () =>
                    {
                        // Resume from existing .part file if present; WTelegram reads stream.Position as offset
                        using var stream = OpenOrResumePartFile(partPath);
                        using var _ = downloadToken.Register(() => { try { stream.Dispose(); } catch { } });
                        await Client.DownloadFileAsync(document, stream, null, progress);
                        return true;
                    }, downloadToken);
                    File.Move(partPath, pathFolderLocation, overwrite: true);
                    FileDownloadIndex.MarkDownloaded(document.ID);
                    OnComplete?.Invoke(chatDto.Name, fileName, true);
                    return new ResultExecute(chatDto.Name) { IsSuccess = true, FileName = fileName, FilePath = pathFolderLocation };
                }
                catch (OperationCanceledException)
                {
                    if (userCancelToken.IsCancellationRequested)
                        DeletePartialFile(partPath);
                    CancellationRegistry.Remove(CancellationRegistry.MakeKey(chatDto.Name, fileName));
                    return new ResultExecute(chatDto.Name) { IsSuccess = false, FileName = fileName, ErrorMessage = "Cancelled by user" };
                }
                catch (Exception) when (downloadToken.IsCancellationRequested)
                {
                    // Inactivity timeout — keep .part file for resume
                    CancellationRegistry.Remove(CancellationRegistry.MakeKey(chatDto.Name, fileName));
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
