using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace TelegramAutoDownload.Services
{
    /// <summary>
    /// Ensures yt-dlp.exe is present and up-to-date.
    /// On startup: downloads if missing, then checks GitHub for a newer version and updates silently.
    /// </summary>
    public static class YtdlpService
    {
        private const string GitHubApiUrl =
            "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest";

        /// <summary>Writable tools folder in %APPDATA%\TelegramAutoDownload\tools\</summary>
        public static string ToolsFolder => AppPaths.ToolsDir;

        public static string ExePath => Path.Combine(ToolsFolder, "yt-dlp.exe");

        /// <summary>
        /// Status message updated during update checks (for MainWindow footer display).
        /// </summary>
        public static string StatusMessage { get; private set; } = string.Empty;
        public static event Action<string>? StatusChanged;

        /// <summary>
        /// Ensures yt-dlp.exe exists and checks for updates. Never throws.
        /// </summary>
        public static async Task EnsureAsync()
        {
            try
            {
                // ToolsFolder is already created by AppPaths static constructor
                // If yt-dlp is bundled with the installer (read-only install dir), copy it to writable AppData
                if (!File.Exists(ExePath))
                {
                    var bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "yt-dlp.exe");
                    if (File.Exists(bundled))
                        File.Copy(bundled, ExePath, overwrite: false);
                }

                string? latestVersion = null;
                string? downloadUrl = null;

                // Query GitHub for the latest release
                try
                {
                    using var http = new HttpClient();
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("TelegramAutoDownload/2.2");
                    http.Timeout = TimeSpan.FromSeconds(15);

                    var json = await http.GetStringAsync(GitHubApiUrl);
                    var obj = JObject.Parse(json);
                    latestVersion = obj["tag_name"]?.ToString()?.TrimStart('v');
                    downloadUrl = obj["assets"]?
                        .FirstOrDefault(a => a["name"]?.ToString() == "yt-dlp.exe")?
                        ["browser_download_url"]?.ToString();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "yt-dlp: Could not fetch GitHub release info");
                }

                // Download if missing
                if (!File.Exists(ExePath))
                {
                    SetStatus("yt-dlp: Downloading...");
                    await DownloadAsync(downloadUrl ?? $"https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe");
                    SetStatus("yt-dlp: Ready");
                    return;
                }

                // Check current version
                string? currentVersion = GetCurrentVersion();
                if (currentVersion == null || latestVersion == null) return;

                if (string.Compare(latestVersion, currentVersion, StringComparison.OrdinalIgnoreCase) > 0
                    && downloadUrl != null)
                {
                    SetStatus($"yt-dlp: Updating {currentVersion} → {latestVersion}...");
                    Log.Information("yt-dlp: updating from {Current} to {Latest}", currentVersion, latestVersion);
                    await DownloadAsync(downloadUrl);
                    SetStatus($"yt-dlp: Updated to {latestVersion}");
                    Log.Information("yt-dlp: update complete");
                }
                else
                {
                    SetStatus($"yt-dlp: {currentVersion} (up to date)");
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "yt-dlp: EnsureAsync failed");
                SetStatus("yt-dlp: Update check failed");
            }
        }

        private static async Task DownloadAsync(string url)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TelegramAutoDownload/2.2");
            http.Timeout = TimeSpan.FromMinutes(10);
            var bytes = await http.GetByteArrayAsync(url);
            string tmp = ExePath + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes);
            File.Move(tmp, ExePath, overwrite: true);
        }

        private static string? GetCurrentVersion()
        {
            try
            {
                var psi = new ProcessStartInfo(ExePath, "--version")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                string? version = proc?.StandardOutput.ReadLine()?.Trim();
                proc?.WaitForExit(3000);
                return version;
            }
            catch { return null; }
        }

        private static void SetStatus(string msg)
        {
            StatusMessage = msg;
            StatusChanged?.Invoke(msg);
        }
    }
}
