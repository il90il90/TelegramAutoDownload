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

        // Known social-media / video-sharing domains handled by yt-dlp.
        // Add more entries here as needed — yt-dlp supports 1000+ sites.
        private static readonly string[] _supportedDomains =
        [
            // Video platforms
            "youtube.com", "youtu.be",
            "vimeo.com",
            "dailymotion.com",
            "twitch.tv", "clips.twitch.tv",
            "streamable.com",
            "rumble.com",
            "odysee.com",
            "bitchute.com",
            // Social networks
            "facebook.com", "fb.watch", "fb.com",
            "instagram.com",
            "tiktok.com",
            "x.com", "twitter.com",
            "reddit.com", "v.redd.it",
            "linkedin.com",
            "pinterest.com",
            "snapchat.com",
            "threads.net",
            // Short-link / media CDNs often used for social clips
            "t.co",
            // TikTok mobile short links
            "vm.tiktok.com", "vt.tiktok.com",
        ];

        public override bool CanHandle(Config config)
        {
            var text = config.Text;
            if (!text.StartsWith("http://") && !text.StartsWith("https://"))
                return false;

            // Extract the hostname from the URL
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
                return false;

            var host = uri.Host.ToLowerInvariant();
            // Strip "www." prefix for comparison
            if (host.StartsWith("www."))
                host = host[4..];

            return Array.Exists(_supportedDomains, d => host == d || host.EndsWith("." + d));
        }

        public override async Task<ResultExecute> ExecuteAsync(Config config)
        {
            // Prefer writable AppData tools folder; fall back to install dir (for portable/dev use)
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

            // Use friendly platform name as subfolder (e.g. YouTube, Facebook, TikTok)
            var platformName = GetPlatformName(config.Text);
            var outputFolder = Path.Combine(config.PathSaveFile, platformName, config.ChatName);
            if (!Directory.Exists(outputFolder))
                Directory.CreateDirectory(outputFolder);

            // Limit title to 120 bytes to avoid Windows MAX_PATH overflow on long or Unicode titles
            var outputTemplate = Path.Combine(outputFolder, "%(title).120B.%(ext)s");

            // Optional cookies file — place in AppData tools folder or install dir
            var cookiesAppData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TelegramAutoDownload", "tools", "cookies.txt");
            var cookiesPath = File.Exists(cookiesAppData)
                ? cookiesAppData
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "cookies.txt");

            // Use the full URL as the identifier (shown in Telegram notifications)
            var tempName = config.Text.Length > 80 ? config.Text[..80] + "…" : config.Text;

            // Register a cancellation token so the UI can cancel this download
            var cancelKey = CancellationRegistry.MakeKey(config.ChatName, tempName);
            var cancelToken = CancellationRegistry.Register(cancelKey);

            string downloadedFile = string.Empty;
            string actualFilePath = string.Empty;
            bool hasError = false;
            string errorMessage = string.Empty;

            try
            {
                // Build the yt-dlp command using the per-chat quality setting.
                var format = YtdlpFormatHelper.GetFormatString(config.YtdlpQuality);
                var isAudioOnly = YtdlpFormatHelper.IsAudioOnly(config.YtdlpQuality);

                var builder = new Ytdlp(ytdlpPath)
                    .WithFormat(format)
                    .WithOutputTemplate(outputTemplate)
                    .WithNoPlaylist();

                // Merging into MP4 only makes sense for video+audio; skip for audio-only downloads
                if (!isAudioOnly)
                    builder = builder.WithMergeOutputFormat("mp4");

                // Attach cookies.txt if present (enables X, Instagram, TikTok authenticated downloads)
                if (File.Exists(cookiesPath))
                    builder = builder.WithCookiesFile(cookiesPath);

                await using var ytdlp = builder;

                // Report download started (0%)
                OnProgress?.Invoke(config.ChatName, tempName, platformName, 0, 0, 0);

                ytdlp.OnProgressDownload += (s, e) =>
                {
                    OnProgress?.Invoke(config.ChatName, tempName, platformName, e.Percent, 0, 0);
                };

                ytdlp.OnOutputMessage += (s, msg) =>
                {
                    // yt-dlp prints "[download] Destination: /path/to/file.mp4" when it starts writing
                    if (!string.IsNullOrWhiteSpace(msg) && msg.Contains("Destination:"))
                    {
                        var idx = msg.IndexOf("Destination:");
                        if (idx >= 0)
                            actualFilePath = msg[(idx + "Destination:".Length)..].Trim();
                    }
                };

                ytdlp.OnCompleteDownload += (s, msg) =>
                {
                    // msg from Ytdlp.NET is a status string, not the file path;
                    // use actualFilePath captured from OnOutputMessage if available
                    downloadedFile = !string.IsNullOrEmpty(actualFilePath) ? actualFilePath : (msg ?? string.Empty);
                };

                ytdlp.OnErrorMessage += (s, err) =>
                {
                    // Only treat as error if it's not just a warning
                    if (!string.IsNullOrWhiteSpace(err) && !err.TrimStart().StartsWith("WARNING:"))
                    {
                        hasError = true;
                        errorMessage = MapKnownError(config.Text, err);
                    }
                };

                await ytdlp.DownloadAsync(config.Text, cancelToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException || cancelToken.IsCancellationRequested)
            {
                CancellationRegistry.Remove(cancelKey);
                return new ResultExecute(config.ChatName) { IsSuccess = false, ErrorMessage = "Cancelled" };
            }
            catch (Exception ex)
            {
                OnComplete?.Invoke(config.ChatName, tempName, false);
                CancellationRegistry.Remove(cancelKey);
                return new ResultExecute(config.ChatName) { IsSuccess = false, ErrorMessage = ex.Message, NotificationKey = tempName };
            }

            // If file was downloaded, consider it a success even if there were warnings
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
                return new ResultExecute(config.ChatName) { IsSuccess = false, ErrorMessage = errorMessage, NotificationKey = tempName };
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
        /// <summary>
        /// Maps known yt-dlp error strings to user-friendly messages that explain
        /// how to resolve platform authentication or regional restrictions.
        /// </summary>
        private static string MapKnownError(string url, string rawError)
        {
            var platform = GetPlatformName(url);
            var hint = string.Empty;

            if (rawError.Contains("empty media response") || rawError.Contains("not granting access") ||
                rawError.Contains("login") || rawError.Contains("authentication") ||
                rawError.Contains("cookies") || rawError.Contains("logged-in") ||
                rawError.Contains("logged in"))
            {
                hint = $"{platform} requires authentication. Add a cookies.txt file to %APPDATA%\\TelegramAutoDownload\\tools\\cookies.txt";
            }
            else if (rawError.Contains("IP address is blocked") || rawError.Contains("geo") ||
                     rawError.Contains("not available in your country"))
            {
                hint = $"{platform} blocked this request (IP/region restriction). A VPN or cookies.txt may be required.";
            }

            return string.IsNullOrEmpty(hint) ? rawError : $"{hint}\n\nDetail: {rawError}";
        }

        /// <summary>
        /// Returns a friendly platform name from the URL (e.g. "YouTube", "Facebook").
        /// Used as the subfolder name so downloads are organised by source.
        /// </summary>
        private static string GetPlatformName(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return "SocialMedia";

            var host = uri.Host.ToLowerInvariant();
            if (host.StartsWith("www.")) host = host[4..];

            return host switch
            {
                "youtube.com" or "youtu.be"               => "YouTube",
                "facebook.com" or "fb.watch" or "fb.com"  => "Facebook",
                "instagram.com"                            => "Instagram",
                "tiktok.com" or "vm.tiktok.com"
                    or "vt.tiktok.com"                     => "TikTok",
                "x.com" or "twitter.com" or "t.co"        => "X",
                "reddit.com" or "v.redd.it"               => "Reddit",
                "twitch.tv" or "clips.twitch.tv"           => "Twitch",
                "vimeo.com"                                => "Vimeo",
                "dailymotion.com"                          => "Dailymotion",
                "linkedin.com"                             => "LinkedIn",
                "pinterest.com"                            => "Pinterest",
                "snapchat.com"                             => "Snapchat",
                "threads.net"                              => "Threads",
                "rumble.com"                               => "Rumble",
                "odysee.com"                               => "Odysee",
                "bitchute.com"                             => "Bitchute",
                "streamable.com"                           => "Streamable",
                _                                          => "SocialMedia"
            };
        }
    }
}
