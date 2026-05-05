using BasePlugins;
using ManuHub.Ytdlp.NET;
using System;
using System.IO;
using System.Threading.Tasks;

namespace SocialMediaPlugin
{
    public class SocialMediaPlugin<TMessage> : BasePlugin<TMessage>
    {
        public override string PluginName => "SocialMedia";
        public override int Priority => 2;

        public override bool CanHandle(Config config)
        {
            // Handle any http/https URL that is NOT a YouTube link (YouTube has its own plugin)
            return (config.Text.StartsWith("http://") || config.Text.StartsWith("https://"))
                && !config.Text.Contains("youtu");
        }

        public override async Task<ResultExecute> ExecuteAsync(Config config)
        {
            var ytdlpPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "yt-dlp.exe");
            if (!File.Exists(ytdlpPath))
            {
                return new ResultExecute(config.ChatName)
                {
                    IsSuccess = false,
                    ErrorMessage = "yt-dlp.exe not found in tools\\ folder"
                };
            }

            var outputFolder = Path.Combine(config.PathSaveFile, "SocialMedia", config.ChatName);
            if (!Directory.Exists(outputFolder))
                Directory.CreateDirectory(outputFolder);

            string downloadedFile = string.Empty;
            bool hasError = false;
            string errorMessage = string.Empty;

            try
            {
                await using var ytdlp = new Ytdlp(ytdlpPath)
                    .WithFormat("best[ext=mp4]/best")
                    .WithOutputFolder(outputFolder);

                ytdlp.OnProgressDownload += (s, e) =>
                {
                    // Best-effort progress reporting
                };

                ytdlp.OnCompleteDownload += (s, msg) =>
                {
                    downloadedFile = msg ?? string.Empty;
                };

                ytdlp.OnErrorMessage += (s, err) =>
                {
                    hasError = true;
                    errorMessage = err ?? string.Empty;
                };

                await ytdlp.DownloadAsync(config.Text);
            }
            catch (Exception)
            {
                // Unsupported site or yt-dlp process error; silent fallback to next plugin
                return new ResultExecute(config.ChatName) { IsSuccess = false };
            }

            if (hasError)
            {
                // Silent failure allows DownloadPlugin to handle it as a fallback
                return new ResultExecute(config.ChatName) { IsSuccess = false };
            }

            return new ResultExecute(config.ChatName)
            {
                IsSuccess = true,
                FileName = Path.GetFileName(downloadedFile)
            };
        }
    }
}
