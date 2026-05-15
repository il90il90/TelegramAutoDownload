using System;

namespace TelegramAutoDownload.Models
{
    /// <summary>Navigates the log viewer to a specific file entry.</summary>
    public sealed class LogPointer
    {
        public required string FilePath { get; init; }
        /// <summary>Substring to find inside the log file (prefer a distinctive part of the message).</summary>
        public required string SearchText { get; init; }
        public DateTimeOffset Timestamp { get; init; }
        public string Level { get; init; } = "WRN";
        public string Summary { get; init; } = string.Empty;
    }
}
