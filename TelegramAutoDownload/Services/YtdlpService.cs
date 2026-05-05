using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace TelegramAutoDownload.Services
{
    /// <summary>
    /// Ensures yt-dlp.exe is present in the tools folder, downloading it from GitHub if missing.
    /// </summary>
    public static class YtdlpService
    {
        private const string DownloadUrl =
            "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

        public static string ToolsFolder =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");

        public static string ExePath =>
            Path.Combine(ToolsFolder, "yt-dlp.exe");

        /// <summary>
        /// Downloads yt-dlp.exe if not present. Runs silently; never throws.
        /// </summary>
        public static async Task EnsureAsync()
        {
            try
            {
                if (File.Exists(ExePath)) return;

                Directory.CreateDirectory(ToolsFolder);

                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromMinutes(5);
                var bytes = await http.GetByteArrayAsync(DownloadUrl);
                await File.WriteAllBytesAsync(ExePath, bytes);
            }
            catch
            {
                // Non-critical — SocialMedia plugin will report the missing file
            }
        }
    }
}
