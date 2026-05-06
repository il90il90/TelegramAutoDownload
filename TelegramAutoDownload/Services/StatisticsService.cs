using Newtonsoft.Json;
using Serilog;
using System;
using System.IO;
using System.Threading;

namespace TelegramAutoDownload.Services
{
    /// <summary>
    /// Persists all-time download statistics to %APPDATA%\TelegramAutoDownload\stats.json.
    /// Automatically subscribes to DownloadProgressService.DownloadCompleted on construction.
    /// </summary>
    public sealed class StatisticsService
    {
        private static readonly Lazy<StatisticsService> _instance = new(() => new());
        public static StatisticsService Instance => _instance.Value;

        private StatsData _data = new();
        private readonly string _path = AppPaths.StatsFile;

        // Debounce timer: batches rapid save calls into a single write every 5 s
        private System.Threading.Timer? _saveTimer;
        private readonly object _saveLock = new();

        public long TotalFilesAllTime => _data.TotalFilesAllTime;
        public long TotalBytesAllTime => _data.TotalBytesAllTime;
        public DateTime StatsStart    => _data.StatsStart;

        public event Action? Changed;

        private StatisticsService()
        {
            Load();
            DownloadProgressService.Instance.DownloadCompleted += OnDownloadCompleted;
        }

        private void OnDownloadCompleted(string chatName, string fileName, long bytes, double durationSec)
        {
            Interlocked.Increment(ref _data.TotalFilesAllTime);
            Interlocked.Add(ref _data.TotalBytesAllTime, bytes);
            Changed?.Invoke();
            ScheduleSave();
        }

        private void ScheduleSave()
        {
            lock (_saveLock)
            {
                _saveTimer?.Dispose();
                _saveTimer = new System.Threading.Timer(_ => Save(), null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
            }
        }

        public void Save()
        {
            try
            {
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(_data, Formatting.Indented));
                File.Move(tmp, _path, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "StatisticsService: failed to save stats");
            }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_path))
                    _data = JsonConvert.DeserializeObject<StatsData>(File.ReadAllText(_path)) ?? new();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "StatisticsService: failed to load stats, starting fresh");
                _data = new();
            }
        }

        public void Reset()
        {
            _data = new StatsData { StatsStart = DateTime.UtcNow };
            Save();
            Changed?.Invoke();
        }
    }

    internal class StatsData
    {
        [JsonProperty]
        public long TotalFilesAllTime;

        [JsonProperty]
        public long TotalBytesAllTime;

        public DateTime StatsStart { get; set; } = DateTime.UtcNow;
    }
}
