using System.Collections.Concurrent;
using System.Threading;

namespace BasePlugins
{
    /// <summary>
    /// Shared static registry that lets plugins register a CancellationTokenSource
    /// and the UI layer cancel it — without creating a circular project dependency.
    /// </summary>
    public static class CancellationRegistry
    {
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> _tokens = new();

        /// <summary>Creates and stores a new CTS for the given key. Returns the associated token.
        /// If a CTS already exists for the key it is disposed before being replaced.</summary>
        public static CancellationToken Register(string key)
        {
            var newCts = new CancellationTokenSource();
            // AddOrUpdate: if a stale CTS exists (e.g. from a previous retry), dispose it atomically
            CancellationTokenSource? stale = null;
            _tokens.AddOrUpdate(key,
                _ => newCts,
                (_, existing) => { stale = existing; return newCts; });
            // Dispose outside the factory delegate so we never hold internal locks while disposing
            try { stale?.Dispose(); } catch { }
            return newCts.Token;
        }

        /// <summary>Cancels the CTS for the given key (no-op if not found).</summary>
        public static void Cancel(string key)
        {
            if (_tokens.TryGetValue(key, out var cts))
                cts.Cancel();
        }

        /// <summary>Removes and disposes the CTS for the given key.</summary>
        public static void Remove(string key)
        {
            if (_tokens.TryRemove(key, out var cts))
                cts.Dispose();
        }

        /// <summary>
        /// Cancels every currently registered token.
        /// Used by "Cancel All" so that downloads are stopped regardless of whether the
        /// per-item CancellationKey has been assigned to the UI item yet.
        /// Tokens remain in the registry until each download task calls Remove() on exit.
        /// </summary>
        public static void CancelAll()
        {
            foreach (var kvp in _tokens)
            {
                try { kvp.Value.Cancel(); } catch { }
            }
        }

        /// <summary>Returns the number of currently registered cancellation tokens.</summary>
        public static int Count => _tokens.Count;

        public static string MakeKey(string chatName, string fileName) => $"{chatName}|{fileName}";
    }
}
