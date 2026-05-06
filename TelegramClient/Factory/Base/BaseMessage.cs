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
        /// Creates a WTelegram ProgressCallback and a CancellationToken for this download.
        /// The token is registered in CancellationRegistry so the UI cancel button and the
        /// stuck-download watchdog can both abort the transfer.
        /// The caller should register <c>token.Register(() => stream.Dispose())</c> so that
        /// a hung DownloadFileAsync (no callbacks) is also force-interrupted when cancelled.
        /// </summary>
        protected (Client.ProgressCallback? callback, System.Threading.CancellationToken token)
            MakeProgress(string chatName, string fileName, long totalBytes)
        {
            var cancelKey = CancellationRegistry.MakeKey(chatName, fileName);
            var token = CancellationRegistry.Register(cancelKey);

            Client.ProgressCallback? callback = OnProgress == null ? null :
                (transmitted, total) =>
                {
                    token.ThrowIfCancellationRequested();
                    long effectiveTotal = total > 0 ? total : totalBytes;
                    double pct = effectiveTotal > 0 ? transmitted * 100.0 / effectiveTotal : 0;
                    OnProgress.Invoke(chatName, fileName, TypeMessage.ToString(), Math.Min(99, pct), transmitted, effectiveTotal);
                };

            return (callback, token);
        }

        /// <summary>Silently deletes a partially downloaded file after a cancelled download.</summary>
        protected static void DeletePartialFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        /// <summary>
        /// Executes a download action with automatic retry (exponential backoff).
        /// Does NOT retry on OperationCanceledException or when <paramref name="ct"/> is cancelled
        /// (covers IOException / ObjectDisposedException caused by forced stream closure).
        /// </summary>
        protected static async Task<T> WithRetryAsync<T>(
            Func<Task<T>> action,
            System.Threading.CancellationToken ct = default,
            int maxAttempts = 3)
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    return await action();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch when (ct.IsCancellationRequested)
                {
                    // Stream was disposed by watchdog or user cancel — do not retry
                    throw;
                }
                catch when (++attempt < maxAttempts)
                {
                    int delayMs = attempt == 1 ? 2000 : 5000;
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }
            }
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
