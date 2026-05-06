using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TelegramClient
{
    /// <summary>
    /// Persistent index of downloaded Telegram document IDs.
    /// Each document in Telegram has a unique 64-bit ID that identifies its content.
    /// Using this as a dedup key is equivalent to a hash — it is guaranteed unique per
    /// distinct file and is checked instantly without reading any file bytes.
    /// The index is stored in AppData and survives app restarts.
    /// </summary>
    public static class FileDownloadIndex
    {
        private static readonly string IndexPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TelegramAutoDownload", "downloaded_ids.json");

        private static HashSet<long> _ids = new();
        private static readonly object _lock = new();

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
        /// Records a document ID as downloaded. Persists the index to disk.
        /// Call after a successful download completes.
        /// </summary>
        public static void MarkDownloaded(long documentId)
        {
            lock (_lock)
            {
                if (_ids.Add(documentId))
                    Save();
            }
        }

        /// <summary>
        /// Removes a document ID from the index (e.g. stale entry: ID is recorded but file no longer on disk).
        /// The next download attempt will treat the file as new.
        /// </summary>
        public static void Remove(long documentId)
        {
            lock (_lock)
            {
                if (_ids.Remove(documentId))
                    Save();
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

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(IndexPath)!);
                var json = JsonSerializer.Serialize(new List<long>(_ids));
                File.WriteAllText(IndexPath, json);
            }
            catch { /* non-critical — index rebuilt on next run from disk scan */ }
        }
    }
}
