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

                // Prefer the Setup EXE so the installer handles admin elevation + file replacement.
                // Fall back to the portable ZIP only if no EXE is present.
                string assetUrl = string.Empty;
                string assetName = string.Empty;
                var assets = obj["assets"] as JArray;
                if (assets != null)
                {
                    string zipUrl = string.Empty, zipName = string.Empty;
                    foreach (var asset in assets)
                    {
                        var name = asset["name"]?.ToString() ?? string.Empty;
                        var url  = asset["browser_download_url"]?.ToString() ?? string.Empty;
                        if (name.EndsWith("_Setup.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            assetUrl  = url;
                            assetName = name;
                            break;
                        }
                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && zipUrl == string.Empty)
                        {
                            zipUrl  = url;
                            zipName = name;
                        }
                    }
                    // Fall back to ZIP if no Setup EXE found
                    if (assetUrl == string.Empty && zipUrl != string.Empty)
                    {
                        assetUrl  = zipUrl;
                        assetName = zipName;
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
        /// Downloads the Setup EXE (preferred) or portable ZIP, then installs.
        /// For the Setup EXE: closes the app and launches the installer (handles UAC + file replacement).
        /// For the ZIP fallback: uses a batch script to xcopy files after the process exits.
        /// </summary>
        public static async Task DownloadAndInstallAsync(ReleaseInfo release, Action<int>? onProgress = null)
        {
            if (string.IsNullOrEmpty(release.AssetUrl))
                throw new InvalidOperationException(
                    "No installer asset is attached to this release yet.\nPlease download manually from GitHub.");

            var tmpFile = Path.Combine(Path.GetTempPath(), release.AssetName);
            bool isInstaller = release.AssetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

            try
            {
                // Download the asset
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"TelegramAutoDownload/{AppVersion.Current}");
                http.Timeout = TimeSpan.FromMinutes(15);

                using var response = await http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                long total = response.Content.Headers.ContentLength ?? 0;
                long received = 0;

                await using (var fs = File.Create(tmpFile))
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

                if (isInstaller)
                {
                    // Run the Inno Setup installer — it handles UAC elevation and file replacement.
                    // /VERYSILENT keeps it quiet; app restart is handled by the installer's [Run] section.
                    Process.Start(new ProcessStartInfo(tmpFile, "/VERYSILENT /NORESTART /CLOSEAPPLICATIONS")
                    {
                        UseShellExecute = true  // Required for UAC prompt to appear
                    });
                    Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
                }
                else
                {
                    // ZIP fallback: extract and xcopy via batch (portable installs only)
                    var tmpExtract = Path.Combine(Path.GetTempPath(), $"TAD_update_{release.Version}");
                    if (Directory.Exists(tmpExtract)) Directory.Delete(tmpExtract, true);
                    System.IO.Compression.ZipFile.ExtractToDirectory(tmpFile, tmpExtract);

                    string srcDir = tmpExtract;
                    var subdirs = Directory.GetDirectories(tmpExtract);
                    if (subdirs.Length == 1) srcDir = subdirs[0];

                    var appDir = AppDomain.CurrentDomain.BaseDirectory;
                    var exeName = Path.GetFileName(Process.GetCurrentProcess().MainModule!.FileName);
                    var pid = Process.GetCurrentProcess().Id;
                    var batchPath = Path.Combine(Path.GetTempPath(), "tad_updater.bat");

                    File.WriteAllText(batchPath, $@"@echo off
:wait
tasklist /fi ""pid eq {pid}"" | findstr /i ""{pid}"" >nul 2>&1
if not errorlevel 1 ( timeout /t 1 /nobreak >nul & goto wait )
xcopy /e /y /i ""{srcDir}\*"" ""{appDir.TrimEnd('\\', '/')}\ ""
start """" ""{Path.Combine(appDir, exeName)}""
del ""%~f0""
");
                    Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{batchPath}\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Auto-update install failed");
                throw;
            }
            finally
            {
                // Don't delete the EXE — the installer needs it after Shutdown()
                if (!isInstaller)
                    try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { }
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
