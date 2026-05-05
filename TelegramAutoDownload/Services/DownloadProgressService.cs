using BasePlugins;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using TelegramAutoDownload.Models;

namespace TelegramAutoDownload.Services
{
    public class DownloadProgressService
    {
        private static readonly Lazy<DownloadProgressService> _instance = new(() => new());
        public static DownloadProgressService Instance => _instance.Value;

        public ObservableCollection<DownloadItem> Downloads { get; } = new();

        // Session statistics (in-memory only)
        private int _totalFilesDownloaded;
        private long _totalBytesDownloaded;

        public int TotalFilesDownloaded => _totalFilesDownloaded;
        public long TotalBytesDownloaded => _totalBytesDownloaded;

        public event Action? StatsChanged;

        /// <summary>
        /// Fired when a download completes successfully: (chatName, fileName, totalBytes, durationSeconds)
        /// </summary>
        public event Action<string, string, long, double>? DownloadCompleted;

        public void AddDownload(string chatName, string fileName, string pluginName, long totalBytes = 0)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
                Downloads.Add(new DownloadItem
                {
                    ChatName = chatName,
                    FileName = fileName,
                    PluginName = pluginName,
                    TotalBytes = totalBytes
                }));
        }

        /// <summary>
        /// Updates download progress. Auto-registers the item if it doesn't exist yet.
        /// Pass bytesDownloaded and totalBytes for accurate speed/ETA.
        /// </summary>
        public void UpdateProgress(string chatName, string fileName, double percent,
            long bytesDownloaded = 0, long totalBytes = 0, string pluginName = "")
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                var item = Downloads.FirstOrDefault(d => d.ChatName == chatName && d.FileName == fileName);

                // Auto-register on first report (e.g. when called before AddDownload)
                if (item == null)
                {
                    var key = CancellationRegistry.MakeKey(chatName, fileName);
                    item = new DownloadItem
                    {
                        ChatName = chatName,
                        FileName = fileName,
                        PluginName = pluginName,
                        TotalBytes = totalBytes,
                        CancellationKey = key
                    };
                    Downloads.Add(item);
                }

                item.Progress = Math.Min(100, Math.Max(0, percent));

                if (bytesDownloaded > 0) item.BytesDownloaded = bytesDownloaded;
                if (totalBytes > 0) item.TotalBytes = totalBytes;
                if (!string.IsNullOrEmpty(pluginName) && string.IsNullOrEmpty(item.PluginName))
                    item.PluginName = pluginName;

                // Calculate elapsed time
                var elapsed = (DateTime.UtcNow - item.StartTime).TotalSeconds;
                if (elapsed < 0.5) return;

                // Speed: based on bytes if available, else estimate from percent
                if (item.BytesDownloaded > 0 && elapsed > 0)
                {
                    double bytesPerSec = item.BytesDownloaded / elapsed;
                    item.Speed = FormatSpeed(bytesPerSec);

                    if (item.TotalBytes > 0 && bytesPerSec > 0)
                    {
                        long remaining = item.TotalBytes - item.BytesDownloaded;
                        item.Eta = FormatEta(remaining / bytesPerSec);
                    }
                    else if (item.Progress > 0 && item.Progress < 100)
                    {
                        double etaSec = elapsed / (item.Progress / 100.0) - elapsed;
                        item.Eta = FormatEta(etaSec);
                    }
                }
                else if (item.Progress > 0 && item.Progress < 100 && elapsed > 0)
                {
                    double etaSec = elapsed / (item.Progress / 100.0) - elapsed;
                    item.Eta = FormatEta(etaSec);
                }
            });
        }

        public void CompleteDownload(string chatName, string fileName, bool success)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                var item = Downloads.FirstOrDefault(d => d.ChatName == chatName && d.FileName == fileName);
                if (item == null) return;

                item.Status = success ? "✔ Done" : "✖ Error";
                item.Progress = success ? 100 : item.Progress;
                item.Speed = "";
                item.Eta = "";

                if (success)
                {
                    System.Threading.Interlocked.Increment(ref _totalFilesDownloaded);
                    long bytes = item.TotalBytes > 0 ? item.TotalBytes : item.BytesDownloaded;
                    System.Threading.Interlocked.Add(ref _totalBytesDownloaded, bytes);
                    StatsChanged?.Invoke();
                    double durationSec = (DateTime.UtcNow - item.StartTime).TotalSeconds;
                    DownloadCompleted?.Invoke(chatName, fileName, bytes, durationSec);
                }

                // Release the cancellation token
                CancellationRegistry.Remove(item.CancellationKey);

                // Auto-remove after 4 seconds
                Task.Delay(4000).ContinueWith(_ =>
                    Application.Current?.Dispatcher.InvokeAsync(() => Downloads.Remove(item)));
            });
        }

        /// <summary>
        /// Cancels the download identified by chatName + fileName.
        /// </summary>
        public void CancelDownload(string chatName, string fileName)
        {
            var key = CancellationRegistry.MakeKey(chatName, fileName);
            CancellationRegistry.Cancel(key);

            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                var item = Downloads.FirstOrDefault(d => d.ChatName == chatName && d.FileName == fileName);
                if (item == null) return;
                item.Status = "✖ Cancelled";
                item.Speed = "";
                item.Eta = "";
                Task.Delay(3000).ContinueWith(_ =>
                    Application.Current?.Dispatcher.InvokeAsync(() => Downloads.Remove(item)));
            });
        }

        private static string FormatSpeed(double bytesPerSec)
        {
            if (bytesPerSec >= 1_048_576) return $"{bytesPerSec / 1_048_576:F1} MB/s";
            if (bytesPerSec >= 1024) return $"{bytesPerSec / 1024:F0} KB/s";
            return $"{bytesPerSec:F0} B/s";
        }

        private static string FormatEta(double seconds)
        {
            if (seconds <= 0 || double.IsInfinity(seconds) || double.IsNaN(seconds)) return "";
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m {ts.Seconds}s";
            return $"{ts.Seconds}s";
        }
    }
}
