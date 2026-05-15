using System;
using System.IO;
using System.Threading.Tasks;

namespace TelegramClient
{
    public static class TelegramSessionHelper
    {
        public static string SessionFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TelegramAutoDownload", "session.dat");

        public static bool SessionFileExists => File.Exists(SessionFilePath);

        /// <summary>Removes a corrupt/expired session so the next start shows login.</summary>
        public static void DeleteSessionFile()
        {
            try
            {
                if (File.Exists(SessionFilePath))
                    File.Delete(SessionFilePath);
            }
            catch { /* best effort */ }
        }

        /// <summary>Waits until session.dat is not exclusively locked by another process.</summary>
        public static async Task WaitForSessionFileUnlockedAsync(int maxWaitMs = 8000)
        {
            if (!SessionFileExists) return;

            var deadline = Environment.TickCount64 + maxWaitMs;
            while (Environment.TickCount64 < deadline)
            {
                if (CanOpenSessionFileExclusively())
                    return;
                await Task.Delay(250).ConfigureAwait(false);
            }
        }

        private static bool CanOpenSessionFileExclusively()
        {
            try
            {
                using var fs = new FileStream(SessionFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public static bool IsAuthKeyError(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is TL.RpcException rpc &&
                    (rpc.Code == 404 || rpc.Message.Contains("AUTH", StringComparison.OrdinalIgnoreCase)))
                    return true;
                if (e.Message.Contains("Auth key not found", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
