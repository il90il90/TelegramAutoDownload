using System;
using WTelegram;

namespace TelegramClient
{
    /// <summary>
    /// Tuning for Telegram MTProto file downloads (WTelegramClient).
    /// The library defaults to only 2 parallel chunk transfers — raising this is the main speed gain.
    /// </summary>
    public static class TelegramDownloadPerf
    {
        /// <summary>Parallel upload/download chunk requests per Client (WTelegram default is 2).</summary>
        public const int ParallelTransfers = 10;

        /// <summary>Bytes per Telegram file part (WTelegram default is 512 KB).</summary>
        public const int FilePartSizeBytes = 1024 * 1024;

        /// <summary>Default concurrent files in the app queue.</summary>
        public const int DefaultDownloadThreads = 6;

        public const int MaxDownloadThreads = 16;
        public const int MinDownloadThreads = 1;

        public const int FileStreamBufferBytes = 1024 * 1024;

        /// <summary>Minimum interval between UI progress updates per download.</summary>
        public static readonly TimeSpan ProgressUiInterval = TimeSpan.FromMilliseconds(200);

        public static void ConfigureClient(Client client)
        {
            if (client.ParallelTransfers < ParallelTransfers)
                client.ParallelTransfers = ParallelTransfers;
            if (client.FilePartSize < FilePartSizeBytes)
                client.FilePartSize = FilePartSizeBytes;
        }

        public static int ClampDownloadThreads(int threads) =>
            Math.Max(MinDownloadThreads, Math.Min(MaxDownloadThreads, threads));
    }
}
