using System;
using System.IO;

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
