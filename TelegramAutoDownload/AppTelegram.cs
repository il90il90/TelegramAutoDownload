using System;
using TelegramClient;

namespace TelegramAutoDownload
{
    /// <summary>One <see cref="TelegramApp"/> per process so session.dat is never opened twice.</summary>
    public static class AppTelegram
    {
        private static readonly object Gate = new();
        private static TelegramApp? _instance;

        public static TelegramApp? Current
        {
            get { lock (Gate) return _instance; }
        }

        public static TelegramApp GetOrCreate(int appId, string apiHash)
        {
            lock (Gate)
            {
                if (_instance != null)
                    return _instance;

                _instance = new TelegramApp(appId, apiHash);
                return _instance;
            }
        }

        /// <summary>Releases the Telegram client and unlocks session.dat (e.g. before showing login).</summary>
        public static void Release()
        {
            lock (Gate)
            {
                if (_instance == null) return;
                try { _instance.DisposeClient(); }
                catch { /* ignore */ }
                _instance = null;
            }
        }
    }
}
