using System;
using System.Reflection;
using WTelegram;

namespace TelegramClient
{
    /// <summary>
    /// Tuning for Telegram MTProto file downloads (WTelegramClient).
    /// </summary>
    public static class TelegramDownloadPerf
    {
        /// <summary>Target parallel chunk requests when the Client exposes ParallelTransfers.</summary>
        public const int ParallelTransfers = 4;

        public const int FilePartSizeBytes = 512 * 1024;

        public const int DefaultDownloadThreads = 6;

        public const int MaxDownloadThreads = 16;
        public const int MinDownloadThreads = 1;

        public const int FileStreamBufferBytes = 1024 * 1024;

        public static readonly TimeSpan ProgressUiInterval = TimeSpan.FromMilliseconds(200);

        public static void ConfigureClient(Client client)
        {
            client.FilePartSize = FilePartSizeBytes;
            TrySetParallelTransfers(client, ParallelTransfers);
        }

        private static void TrySetParallelTransfers(Client client, int target)
        {
            var prop = client.GetType().GetProperty("ParallelTransfers", BindingFlags.Instance | BindingFlags.Public);
            if (prop?.CanWrite != true) return;

            var current = (int)prop.GetValue(client)!;
            if (current < target)
                prop.SetValue(client, target);
        }

        public static int ClampDownloadThreads(int threads) =>
            Math.Max(MinDownloadThreads, Math.Min(MaxDownloadThreads, threads));
    }
}
