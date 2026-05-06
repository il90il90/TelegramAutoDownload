using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace TelegramClient
{
    /// <summary>
    /// Persistent index of downloaded Telegram document IDs.
    /// Each document in Telegram has a unique 64-bit ID that identifies its content.
    /// Using this as a dedup key is equivalent to a hash — it is guaranteed unique per
    /// distinct file and is checked instantly without reading any file bytes.
    /// The index is stored in AppData and survives app restarts.
    ///
    /// Performance: MarkDownloaded does NOT write to disk immediately. Instead a
    /// background timer flushes every 10 seconds so concurrent downloads from multiple
    /// threads never contend on disk I/O. Call Flush() on application exit to ensure
    /// no records are lost.
    /// </summary>
    public static class FileDownloadIndex
    {
        private static readonly string IndexPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TelegramAutoDownload", "downloaded_ids.json");

        private static HashSet<long> _ids = new();
        private static readonly object _lock = new();
        private static volatile bool _dirty = false;

        // Flush dirty records to disk every 10 seconds instead of on every MarkDownloaded call.
        // This prevents O(n) disk writes when many files complete at the same time.
        private static readonly Timer _flushTimer = new Timer(
            _ => FlushIfDirty(), null,
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

        static FileDownloadIndex()
        {
            Load();
        }

        /// <summary>Returns true if a document with this Telegram ID was already downloaded.</summary>
        public static bool IsAlreadyDownloaded(long documentId)
        {
            lock (_lock) return _ids.Contains(documentId);
        }

        /// <summary>
        /// Records a document ID as downloaded. The change is held in memory and flushed
        /// to disk by the background timer (every 10 s). Call <see cref="Flush"/> on exit
        /// to guarantee no records are lost.
        /// </summary>
        public static void MarkDownloaded(long documentId)
        {
            lock (_lock)
            {
                if (_ids.Add(documentId))
                    _dirty = true;
            }
        }

        /// <summary>
        /// Removes a document ID from the index (e.g. stale entry: ID is recorded but file no longer on disk).
        /// Writes to disk immediately because removals are rare and must be durable.
        /// The next download attempt will treat the file as new.
        /// </summary>
        public static void Remove(long documentId)
        {
            lock (_lock)
            {
                if (_ids.Remove(documentId))
                {
                    _dirty = false;
                    SaveInternal();
                }
            }
        }

        /// <summary>
        /// Forces an immediate write of any pending changes to disk.
        /// Should be called on application exit to prevent data loss.
        /// </summary>
        public static void Flush() => FlushIfDirty();

        private static void FlushIfDirty()
        {
            lock (_lock)
            {
                if (!_dirty) return;
                _dirty = false;
                SaveInternal();
            }
        }

        private static void Load()
        {
            try
            {
                if (!File.Exists(IndexPath)) return;
                var json = File.ReadAllText(IndexPath);
                var list = JsonSerializer.Deserialize<List<long>>(json);
                if (list != null)
                    _ids = new HashSet<long>(list);
            }
            catch { /* start with empty index on corruption */ }
        }

        private static void SaveInternal()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(IndexPath)!);
                // Write to a temp file first and then atomically rename to avoid
                // corrupting the index if the app is killed during the write.
                var tmpPath = IndexPath + ".tmp";
                var json = JsonSerializer.Serialize(new List<long>(_ids));
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, IndexPath, overwrite: true);
            }
            catch { /* non-critical — index rebuilt on next run from disk scan */ }
        }
    }
}
