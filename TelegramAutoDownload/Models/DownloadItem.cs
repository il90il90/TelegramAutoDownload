using System;
using System.ComponentModel;

namespace TelegramAutoDownload.Models
{
    public class DownloadItem : INotifyPropertyChanged
    {
        private double _progress;
        private string _status = "Downloading";
        private string _speed = "";
        private string _eta = "";
        private string _progressText = "0%";
        private string _sizeText = "";
        private long _totalBytes;
        private long _bytesDownloaded;

        public string FileName { get; set; } = string.Empty;
        public string ChatName { get; set; } = string.Empty;
        public string PluginName { get; set; } = string.Empty;

        // Stable identity key: Telegram message ID used for deduplication and lookup
        public int MessageId { get; set; }

        // Key used to cancel this download via CancellationRegistry
        public string CancellationKey { get; set; } = string.Empty;

        // Timestamp when this download started (used for speed/ETA calculation)
        public DateTime StartTime { get; set; } = DateTime.UtcNow;

        // Last time a progress update was received — used by the stuck-download watchdog
        public DateTime LastProgressTime { get; set; } = DateTime.UtcNow;

        public double Progress
        {
            get => _progress;
            set
            {
                _progress = value;
                _progressText = $"{value:F0}%";
                OnPropertyChanged(nameof(Progress));
                OnPropertyChanged(nameof(ProgressText));
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(SortOrder));
                OnPropertyChanged(nameof(CanRetry));
            }
        }

        public string Speed
        {
            get => _speed;
            set { _speed = value; OnPropertyChanged(nameof(Speed)); }
        }

        public string Eta
        {
            get => _eta;
            set { _eta = value; OnPropertyChanged(nameof(Eta)); }
        }

        public string ProgressText
        {
            get => _progressText;
        }

        public string SizeText
        {
            get => _sizeText;
            set { _sizeText = value; OnPropertyChanged(nameof(SizeText)); }
        }

        public long TotalBytes
        {
            get => _totalBytes;
            set { _totalBytes = value; UpdateSizeText(); }
        }

        public long BytesDownloaded
        {
            get => _bytesDownloaded;
            set { _bytesDownloaded = value; UpdateSizeText(); }
        }

        private void UpdateSizeText()
        {
            if (_totalBytes > 0)
                SizeText = $"{FormatBytes(_bytesDownloaded)} / {FormatBytes(_totalBytes)}";
            else if (_bytesDownloaded > 0)
                SizeText = FormatBytes(_bytesDownloaded);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
            if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:F0} KB";
            return $"{bytes} B";
        }

        // 0 = Downloading (top), 1 = Queued, 2 = finished/error — used by the CollectionView sort
        public int SortOrder => Status switch
        {
            "⬇ Downloading" => 0,
            "⏳ Queued"     => 1,
            _               => 2
        };

        private Func<Task>? _retryAsync;

        /// <summary>
        /// Set by TelegramApp after a failed download so the UI can offer a Retry button.
        /// Null when retry is not available (e.g. dedup skip, cancelled by user).
        /// </summary>
        public Func<Task>? RetryAsync
        {
            get => _retryAsync;
            set
            {
                _retryAsync = value;
                OnPropertyChanged(nameof(RetryAsync));
                OnPropertyChanged(nameof(CanRetry));
            }
        }

        /// <summary>True when the item failed and a retry callback is available.</summary>
        public bool CanRetry => _retryAsync != null &&
            (Status == "✖ Error" || Status == "✖ Timeout");

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
