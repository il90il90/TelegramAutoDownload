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

        /// <summary>Creates and stores a new CTS for the given key. Returns the associated token.</summary>
        public static CancellationToken Register(string key)
        {
            var cts = new CancellationTokenSource();
            _tokens[key] = cts;
            return cts.Token;
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

        public static string MakeKey(string chatName, string fileName) => $"{chatName}|{fileName}";
    }
}
