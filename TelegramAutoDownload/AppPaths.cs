using System;
using System.IO;

namespace TelegramAutoDownload
{
    /// <summary>
    /// Central location for all user-writable paths.
    /// Writes go to %APPDATA%\TelegramAutoDownload\ so the app works correctly
    /// when installed under Program Files (read-only for normal users).
    /// </summary>
    public static class AppPaths
    {
        public static readonly string DataDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "TelegramAutoDownload");

        public static string ConfigFile => Path.Combine(DataDir, "config.txt");
        public static string LogsDir    => Path.Combine(DataDir, "logs");
        public static string ToolsDir   => Path.Combine(DataDir, "tools");
        public static string EnvFile    => Path.Combine(DataDir, ".env");

        static AppPaths()
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(LogsDir);
            Directory.CreateDirectory(ToolsDir);
        }
    }
}
