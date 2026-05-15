namespace TelegramAutoDownload
{
    /// <summary>
    /// Single source of truth for the rolling file sink so every app session uses the same line format
    /// (avoids mixed timestamp styles inside the same log file across upgrades).
    /// </summary>
    public static class SerilogFileSettings
    {
        /// <summary>File lines: timestamp, level, message; blank line after exceptions for readability.</summary>
        public const string FileOutputTemplate =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}  [{Level:u3}]  {Message:lj}{NewLine}{Exception}{NewLine}";
    }
}
