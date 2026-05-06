using BasePlugins;
using ManuHub.Ytdlp.NET;
using System;
using System.IO;
using System.Threading.Tasks;

namespace YoutubePlugin
{
    /// <summary>
    /// YouTube-specific plugin backed by yt-dlp.
    /// Runs as a secondary fallback (Priority=10) after SocialMediaPlugin (Priority=2),
    /// which also handles YouTube via yt-dlp. This plugin preserves a per-channel
    /// subfolder layout: PathSaveFile/YouTube/ChatName/%(channel)s/
    /// </summary>
    public class YouTubePlugin<TMessage> : BasePlugin<TMessage>
    {
        public override string PluginName => "YouTube";
        public override int Priority => 10;

        public override bool CanHandle(Config config)
        {
            return config.Text.StartsWith("https://youtu") || config.Text.StartsWith("https://www.youtu");
        }

        public override async Task<ResultExecute> ExecuteAsync(Config config)
        {
            // Resolve yt-dlp executable (AppData tools folder preferred, then install dir)
            var appDataTools = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TelegramAutoDownload", "tools", "yt-dlp.exe");
            var installTools = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "yt-dlp.exe");
            var ytdlpPath = File.Exists(appDataTools) ? appDataTools : installTools;

            if (!File.Exists(ytdlpPath))
            {
                return new ResultExecute(config.ChatName)
                {
                    IsSuccess = false,
                    ErrorMessage = "yt-dlp.exe not found. It will be downloaded automatically on next startup."
                };
            }

            // Output layout: PathSaveFile/YouTube/ChatName/%(channel)s/%(title)s.%(ext)s
            var baseFolder = Path.Combine(config.PathSaveFile, PluginName, config.ChatName);
            if (!Directory.Exists(baseFolder))
                Directory.CreateDirectory(baseFolder);

            // %(channel)s creates a per-channel subfolder; yt-dlp sanitises names automatically
            var outputTemplate = Path.Combine(baseFolder, "%(channel)s", "%(title)s.%(ext)s");

            // Optional cookies file for authenticated downloads
            var cookiesAppData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TelegramAutoDownload", "tools", "cookies.txt");
            var cookiesPath = File.Exists(cookiesAppData)
                ? cookiesAppData
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "cookies.txt");

            var tempName = config.Text.Length > 80 ? config.Text[..80] + "…" : config.Text;

            var cancelKey = CancellationRegistry.MakeKey(config.ChatName, tempName);
            var cancelToken = CancellationRegistry.Register(cancelKey);

            string downloadedFile = string.Empty;
            string actualFilePath = string.Empty;
            bool hasError = false;
            string errorMessage = string.Empty;

            try
            {
                var format = YtdlpFormatHelper.GetFormatString(config.YtdlpQuality);
                var isAudioOnly = YtdlpFormatHelper.IsAudioOnly(config.YtdlpQuality);

                var builder = new Ytdlp(ytdlpPath)
                    .WithFormat(format)
                    .WithOutputTemplate(outputTemplate)
                    .WithNoPlaylist();

                if (!isAudioOnly)
                    builder = builder.WithMergeOutputFormat("mp4");

                if (File.Exists(cookiesPath))
                    builder = builder.WithCookiesFile(cookiesPath);

                await using var ytdlp = builder;

                OnProgress?.Invoke(config.ChatName, tempName, PluginName, 0, 0, 0);

                ytdlp.OnProgressDownload += (s, e) =>
                {
                    OnProgress?.Invoke(config.ChatName, tempName, PluginName, e.Percent, 0, 0);
                };

                ytdlp.OnOutputMessage += (s, msg) =>
                {
                    if (!string.IsNullOrWhiteSpace(msg) && msg.Contains("Destination:"))
                    {
                        var idx = msg.IndexOf("Destination:");
                        if (idx >= 0)
                            actualFilePath = msg[(idx + "Destination:".Length)..].Trim();
                    }
                };

                ytdlp.OnCompleteDownload += (s, msg) =>
                {
                    downloadedFile = !string.IsNullOrEmpty(actualFilePath) ? actualFilePath : (msg ?? string.Empty);
                };

                ytdlp.OnErrorMessage += (s, err) =>
                {
                    if (!string.IsNullOrWhiteSpace(err) && !err.TrimStart().StartsWith("WARNING:"))
                    {
                        hasError = true;
                        errorMessage = err;
                    }
                };

                await ytdlp.DownloadAsync(config.Text, cancelToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException || cancelToken.IsCancellationRequested)
            {
                CancellationRegistry.Remove(cancelKey);
                return new ResultExecute(config.ChatName) { IsSuccess = false, ErrorMessage = "Cancelled" };
            }
            catch (Exception)
            {
                OnComplete?.Invoke(config.ChatName, tempName, false);
                CancellationRegistry.Remove(cancelKey);
                return new ResultExecute(config.ChatName) { IsSuccess = false };
            }

            if (!string.IsNullOrEmpty(downloadedFile))
            {
                var realName = File.Exists(downloadedFile) ? Path.GetFileName(downloadedFile) : downloadedFile;
                OnComplete?.Invoke(config.ChatName, tempName, true);
                CancellationRegistry.Remove(cancelKey);
                return new ResultExecute(config.ChatName)
                {
                    IsSuccess = true,
                    FileName = realName,
                    FilePath = File.Exists(downloadedFile) ? downloadedFile : string.Empty,
                    NotificationKey = tempName
                };
            }

            if (hasError)
            {
                OnComplete?.Invoke(config.ChatName, tempName, false);
                CancellationRegistry.Remove(cancelKey);
                return new ResultExecute(config.ChatName) { IsSuccess = false, NotificationKey = tempName };
            }

            OnComplete?.Invoke(config.ChatName, tempName, true);
            CancellationRegistry.Remove(cancelKey);
            return new ResultExecute(config.ChatName)
            {
                IsSuccess = true,
                FileName = Path.GetFileName(downloadedFile),
                NotificationKey = tempName
            };
        }
    }
}
