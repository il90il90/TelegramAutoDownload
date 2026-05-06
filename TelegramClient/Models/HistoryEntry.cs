using System;

namespace TelegramClient.Models
{
    /// <summary>
    /// A single message entry written to a chat's JSONL history file.
    /// One JSON object per line; appended as messages arrive and bulk-written during export.
    /// </summary>
    public class HistoryEntry
    {
        public int           Id              { get; set; }
        public DateTimeOffset Date            { get; set; }
        public long          SenderId        { get; set; }
        public string?       SenderName      { get; set; }

        /// <summary>Message text body (may be empty for media-only messages).</summary>
        public string        Text            { get; set; } = string.Empty;

        /// <summary>
        /// "Photo", "Video", "Audio", "Document", or null for plain-text messages.
        /// </summary>
        public string?       MediaType       { get; set; }

        public string?       FileName        { get; set; }

        /// <summary>True when the message was forwarded from another chat.</summary>
        public bool          IsForwarded     { get; set; }

        /// <summary>Display name of the original sender (forwarded messages only).</summary>
        public string?       ForwardFromName { get; set; }
    }
}
