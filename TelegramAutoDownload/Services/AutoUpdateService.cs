using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace TelegramAutoDownload.Services
{
    public class ReleaseInfo
    {
        public string TagName { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string Changelog { get; init; } = string.Empty;
        public string AssetUrl { get; init; } = string.Empty;
        public string AssetName { get; init; } = string.Empty;
    }

    public static class AutoUpdateService
    {
        private static readonly string ApiUrl =
            $"https://api.github.com/repos/{AppVersion.GitHubOwner}/{AppVersion.GitHubRepo}/releases/latest";

        /// <summary>
        /// Checks GitHub for a newer release.
        /// Returns the release info if a newer version is available, or null if already up to date.
        /// </summary>
        public static async Task<ReleaseInfo?> CheckAsync()
        {
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"TelegramAutoDownload/{AppVersion.Current}");
                http.Timeout = TimeSpan.FromSeconds(15);

                var json = await http.GetStringAsync(ApiUrl);
                var obj = JObject.Parse(json);

                var tagName = obj["tag_name"]?.ToString() ?? string.Empty;
                var version = tagName.TrimStart('v');
                var changelog = obj["body"]?.ToString() ?? string.Empty;

                if (!IsNewer(version, AppVersion.Current))
                    return null;

                // Find the ZIP asset
                string assetUrl = string.Empty;
                string assetName = string.Empty;
                var assets = obj["assets"] as JArray;
                if (assets != null)
                {
                    foreach (var asset in assets)
                    {
                        var name = asset["name"]?.ToString() ?? string.Empty;
                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            assetUrl = asset["browser_download_url"]?.ToString() ?? string.Empty;
                            assetName = name;
                            break;
                        }
                    }
                }

                return new ReleaseInfo
                {
                    TagName = tagName,
                    Version = version,
                    Changelog = changelog,
                    AssetUrl = assetUrl,
                    AssetName = assetName
                };
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Auto-update check failed");
                return null;
            }
        }

        /// <summary>
        /// Downloads the release zip, extracts next to the current exe, launches
        /// a batch script that waits for the app to exit then copies files over.
        /// </summary>
        public static async Task DownloadAndInstallAsync(ReleaseInfo release, Action<int>? onProgress = null)
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var tmpZip = Path.Combine(Path.GetTempPath(), release.AssetName);
            var tmpExtract = Path.Combine(Path.GetTempPath(), $"TAD_update_{release.Version}");

            try
            {
                // Download
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"TelegramAutoDownload/{AppVersion.Current}");
                http.Timeout = TimeSpan.FromMinutes(15);

                using var response = await http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                long total = response.Content.Headers.ContentLength ?? 0;
                long received = 0;

                await using (var fs = File.Create(tmpZip))
                {
                    await using var stream = await response.Content.ReadAsStreamAsync();
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await stream.ReadAsync(buffer)) > 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, read));
                        received += read;
                        if (total > 0)
                            onProgress?.Invoke((int)(received * 100 / total));
                    }
                }

                onProgress?.Invoke(100);

                // Extract
                if (Directory.Exists(tmpExtract))
                    Directory.Delete(tmpExtract, true);
                ZipFile.ExtractToDirectory(tmpZip, tmpExtract);

                // Write an updater batch script
                var exeName = Path.GetFileName(Process.GetCurrentProcess().MainModule!.FileName);
                var pid = Process.GetCurrentProcess().Id;
                var batchPath = Path.Combine(Path.GetTempPath(), "tad_updater.bat");

                // Find the actual source directory inside the extracted zip
                // (the zip may have a single root folder)
                string srcDir = tmpExtract;
                var subdirs = Directory.GetDirectories(tmpExtract);
                if (subdirs.Length == 1)
                    srcDir = subdirs[0];

                File.WriteAllText(batchPath, $@"@echo off
:: Wait for the app to exit
:wait
tasklist /fi ""pid eq {pid}"" | findstr /i ""{pid}"" >nul 2>&1
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto wait
)
:: Copy new files
xcopy /e /y /i ""{srcDir}\*"" ""{appDir.TrimEnd('\\', '/')}\""
:: Restart
start """" ""{Path.Combine(appDir, exeName)}""
del ""%~f0""
");

                // Launch the batch and exit the app
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{batchPath}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

                Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Auto-update install failed");
                throw;
            }
            finally
            {
                try { if (File.Exists(tmpZip)) File.Delete(tmpZip); } catch { }
            }
        }

        private static bool IsNewer(string candidate, string current)
        {
            if (Version.TryParse(candidate, out var v1) && Version.TryParse(current, out var v2))
                return v1 > v2;
            return string.Compare(candidate, current, StringComparison.OrdinalIgnoreCase) > 0;
        }
    }
}
