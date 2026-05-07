using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using TelegramClient.Models;
using TL;

namespace TelegramClient
{
    /// <summary>
    /// Writes per-chat message histories to JSONL files (one JSON object per line).
    /// File location: {basePath}/History/{SanitizedChatName}.jsonl
    ///
    /// Appending is O(1) — no existing data is loaded.
    /// A full export overwrites the file from the beginning (oldest → newest).
    /// </summary>
    public static class ChatHistoryService
    {
        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        };

        // ── Public API ─────────────────────────────────────────────────────────────

        /// <summary>Returns the JSONL file path for a chat's history.</summary>
        public static string GetHistoryFilePath(string chatType, string chatName, string basePath)
        {
            var name = Sanitize(chatName.Trim(), isFileName: true);
            var dir  = Path.Combine(basePath, "History");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, name + ".jsonl");
        }

        /// <summary>
        /// Appends a single entry to the chat's JSONL history file.
        /// Creates the file if it does not exist.
        /// </summary>
        public static async Task AppendEntryAsync(
            string chatType, string chatName, HistoryEntry entry, string basePath)
        {
            var path = GetHistoryFilePath(chatType, chatName, basePath);
            var line = JsonSerializer.Serialize(entry, _jsonOpts) + Environment.NewLine;
            await File.AppendAllTextAsync(path, line);
        }

        /// <summary>
        /// Overwrites the chat's JSONL file with all provided entries (oldest first).
        /// Use for a full-history export.
        /// </summary>
        public static async Task WriteFullHistoryAsync(
            string chatType, string chatName,
            IEnumerable<HistoryEntry> entries, string basePath)
        {
            var path  = GetHistoryFilePath(chatType, chatName, basePath);
            var lines = entries.Select(e => JsonSerializer.Serialize(e, _jsonOpts));
            await File.WriteAllLinesAsync(path, lines);
        }

        /// <summary>
        /// Reads all entries from an existing JSONL history file.
        /// Lines that cannot be parsed are silently skipped.
        /// </summary>
        public static async Task<List<HistoryEntry>> ReadHistoryAsync(
            string chatType, string chatName, string basePath)
        {
            var path = GetHistoryFilePath(chatType, chatName, basePath);
            if (!File.Exists(path)) return [];

            var result = new List<HistoryEntry>();
            foreach (var line in await File.ReadAllLinesAsync(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<HistoryEntry>(line, _jsonOpts);
                    if (entry != null) result.Add(entry);
                }
                catch { /* skip malformed lines */ }
            }
            return result;
        }

        /// <summary>Creates a <see cref="HistoryEntry"/> from a Telegram message.</summary>
        public static HistoryEntry CreateEntry(Message msg, string? senderName = null)
        {
            string? mediaType = null;
            string? fileName  = null;

            if (msg.media is MessageMediaDocument { document: Document doc })
            {
                mediaType = ClassifyDocument(doc);
                fileName  = doc.Filename;
            }
            else if (msg.media is MessageMediaPhoto)
            {
                mediaType = "Photo";
            }

            return new HistoryEntry
            {
                Id              = msg.ID,
                Date            = new DateTimeOffset(msg.date),
                SenderId        = msg.from_id is PeerUser pu ? pu.user_id : 0,
                SenderName      = senderName,
                Text            = msg.message ?? string.Empty,
                MediaType       = mediaType,
                FileName        = fileName,
                IsForwarded     = msg.fwd_from != null,
                ForwardFromName = msg.fwd_from?.from_name,
            };
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private static string ClassifyDocument(Document doc)
        {
            if (doc.attributes == null) return "Document";
            if (doc.attributes.Any(a => a is DocumentAttributeVideo))          return "Video";
            if (doc.attributes.Any(a => a is DocumentAttributeAudio))          return "Audio";
            if (doc.attributes.Any(a => a is DocumentAttributeImageSize))      return "Image";
            if (doc.attributes.Any(a => a is DocumentAttributeAnimated))       return "GIF";
            if (doc.attributes.Any(a => a is DocumentAttributeSticker))        return "Sticker";
            return "Document";
        }

        internal static string Sanitize(string s, bool isFileName)
        {
            var invalid = isFileName ? Path.GetInvalidFileNameChars() : Path.GetInvalidPathChars();
            foreach (char c in invalid) s = s.Replace(c, '_');
            return s.Trim('_', ' ');
        }
    }
}
