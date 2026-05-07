using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TelegramClient;
using TelegramClient.Models;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    /// <summary>
    /// Tests for the chat-history JSONL export feature:
    ///   - HistoryEntry model
    ///   - ChatHistoryService path generation, append, read, full-write
    ///   - ChatDto.SaveHistory default and persistence
    ///   - Edge cases: empty history, duplicate IDs, unicode chat names
    /// </summary>
    public class ChatHistoryTests : IDisposable
    {
        // Each test gets an isolated temp directory
        private readonly string _baseDir = Path.Combine(
            Path.GetTempPath(), "ChatHistoryTests_" + Guid.NewGuid().ToString("N"));

        public ChatHistoryTests() => Directory.CreateDirectory(_baseDir);
        public void Dispose()
        {
            try { Directory.Delete(_baseDir, recursive: true); } catch { }
        }

        // ── Path generation ──────────────────────────────────────────────────────

        [Fact]
        public void GetHistoryFilePath_ProducesCorrectRelativeLocation()
        {
            var path = ChatHistoryService.GetHistoryFilePath("Channel", "TechNews", _baseDir);

            // History/{ChatName}.jsonl — the Type subdirectory was removed
            path.Should().EndWith("TechNews.jsonl");
            path.Should().Contain("History");
        }

        [Fact]
        public void GetHistoryFilePath_CreatesDirectoryIfMissing()
        {
            var path = ChatHistoryService.GetHistoryFilePath("Group", "NewChat", _baseDir);
            var dir  = Path.GetDirectoryName(path)!;

            Directory.Exists(dir).Should().BeTrue(
                "GetHistoryFilePath must create the History/ folder automatically");
        }

        [Fact]
        public void GetHistoryFilePath_SameName_SameFile_RegardlessOfType()
        {
            // After removing the {Type} subdirectory, same chat name → same file.
            var channelPath = ChatHistoryService.GetHistoryFilePath("Channel", "Alpha", _baseDir);
            var groupPath   = ChatHistoryService.GetHistoryFilePath("Group",   "Alpha", _baseDir);

            channelPath.Should().Be(groupPath,
                because: "chat type is no longer part of the file path — only the chat name matters");
        }

        [Fact]
        public void GetHistoryFilePath_ChatNameWithInvalidChars_IsSanitized()
        {
            var path = ChatHistoryService.GetHistoryFilePath("Channel", "My:Chat/Name<>", _baseDir);
            var fileName = Path.GetFileName(path);

            fileName.Should().NotContainAny(":", "/", "<", ">",
                "invalid filename characters must be replaced");
            path.Should().EndWith(".jsonl");
        }

        [Fact]
        public void GetHistoryFilePath_UnicodeChatName_IsPreserved()
        {
            var path = ChatHistoryService.GetHistoryFilePath("Channel", "עברית קאנל", _baseDir);
            Path.GetFileName(path).Should().Contain("עברית",
                "Unicode characters that are valid in file names must be preserved");
        }

        // ── AppendEntryAsync ─────────────────────────────────────────────────────

        [Fact]
        public async Task AppendEntry_CreatesFileIfMissing()
        {
            var entry = MakeEntry(1, "Hello world");
            await ChatHistoryService.AppendEntryAsync("Channel", "Chat1", entry, _baseDir);

            var path = ChatHistoryService.GetHistoryFilePath("Channel", "Chat1", _baseDir);
            File.Exists(path).Should().BeTrue();
        }

        [Fact]
        public async Task AppendEntry_SingleEntry_IsValidJson()
        {
            var entry = MakeEntry(42, "Test message");
            await ChatHistoryService.AppendEntryAsync("Channel", "Chat2", entry, _baseDir);

            var path = ChatHistoryService.GetHistoryFilePath("Channel", "Chat2", _baseDir);
            var line = (await File.ReadAllLinesAsync(path))
                .First(l => !string.IsNullOrWhiteSpace(l));

            var act = () => JsonDocument.Parse(line);
            act.Should().NotThrow("each appended line must be valid JSON");
        }

        [Fact]
        public async Task AppendEntry_MultipleEntries_ProduceMultipleLines()
        {
            for (int i = 1; i <= 5; i++)
                await ChatHistoryService.AppendEntryAsync("Group", "Chat3", MakeEntry(i, $"msg {i}"), _baseDir);

            var path  = ChatHistoryService.GetHistoryFilePath("Group", "Chat3", _baseDir);
            var lines = (await File.ReadAllLinesAsync(path))
                         .Where(l => !string.IsNullOrWhiteSpace(l))
                         .ToList();

            lines.Should().HaveCount(5, "one JSON line per appended entry");
        }

        [Fact]
        public async Task AppendEntry_PreservesMessageId()
        {
            var entry = MakeEntry(999, "check id");
            await ChatHistoryService.AppendEntryAsync("Channel", "Chat4", entry, _baseDir);

            var entries = await ChatHistoryService.ReadHistoryAsync("Channel", "Chat4", _baseDir);
            entries.Should().ContainSingle()
                   .Which.Id.Should().Be(999);
        }

        [Fact]
        public async Task AppendEntry_PreservesAllFields()
        {
            var at = new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);
            var entry = new HistoryEntry
            {
                Id              = 7,
                Date            = at,
                SenderId        = 12345,
                SenderName      = "Alice",
                Text            = "Hello!",
                MediaType       = "Video",
                FileName        = "clip.mp4",
                IsForwarded     = true,
                ForwardFromName = "Bob"
            };

            await ChatHistoryService.AppendEntryAsync("Channel", "Chat5", entry, _baseDir);
            var read = (await ChatHistoryService.ReadHistoryAsync("Channel", "Chat5", _baseDir)).Single();

            read.Id.Should().Be(7);
            read.SenderId.Should().Be(12345);
            read.SenderName.Should().Be("Alice");
            read.Text.Should().Be("Hello!");
            read.MediaType.Should().Be("Video");
            read.FileName.Should().Be("clip.mp4");
            read.IsForwarded.Should().BeTrue();
            read.ForwardFromName.Should().Be("Bob");
        }

        // ── WriteFullHistoryAsync ────────────────────────────────────────────────

        [Fact]
        public async Task WriteFullHistory_OverwritesExistingFile()
        {
            // Write an initial entry
            await ChatHistoryService.AppendEntryAsync("Channel", "Chat6", MakeEntry(1, "old"), _baseDir);

            // Overwrite with new entries
            var newEntries = Enumerable.Range(10, 3).Select(i => MakeEntry(i, $"new {i}"));
            await ChatHistoryService.WriteFullHistoryAsync("Channel", "Chat6", newEntries, _baseDir);

            var entries = await ChatHistoryService.ReadHistoryAsync("Channel", "Chat6", _baseDir);
            entries.Should().HaveCount(3, "full write must replace existing content");
            entries.Should().NotContain(e => e.Id == 1, "old entry must be gone after full write");
        }

        [Fact]
        public async Task WriteFullHistory_EmptyList_ProducesEmptyFile()
        {
            await ChatHistoryService.WriteFullHistoryAsync("Channel", "Chat7", [], _baseDir);

            var entries = await ChatHistoryService.ReadHistoryAsync("Channel", "Chat7", _baseDir);
            entries.Should().BeEmpty();
        }

        // ── ReadHistoryAsync ─────────────────────────────────────────────────────

        [Fact]
        public async Task ReadHistory_MissingFile_ReturnsEmpty()
        {
            var entries = await ChatHistoryService.ReadHistoryAsync("Channel", "DoesNotExist", _baseDir);
            entries.Should().BeEmpty("missing file must return an empty list, not throw");
        }

        [Fact]
        public async Task ReadHistory_CorruptLine_IsSkipped()
        {
            var path = ChatHistoryService.GetHistoryFilePath("Channel", "Corrupt", _baseDir);
            await File.WriteAllTextAsync(path,
                "{\"id\":1,\"text\":\"ok\"}\nNOT_VALID_JSON\n{\"id\":2,\"text\":\"also ok\"}\n");

            var entries = await ChatHistoryService.ReadHistoryAsync("Channel", "Corrupt", _baseDir);
            entries.Should().HaveCount(2, "corrupt lines must be skipped gracefully");
        }

        [Fact]
        public async Task ReadHistory_RoundTrip_Preserves100Entries()
        {
            var written = Enumerable.Range(1, 100)
                .Select(i => MakeEntry(i, $"message {i}"))
                .ToList();

            await ChatHistoryService.WriteFullHistoryAsync("Channel", "Big", written, _baseDir);
            var read = await ChatHistoryService.ReadHistoryAsync("Channel", "Big", _baseDir);

            read.Should().HaveCount(100);
            read.Select(e => e.Id).Should().BeEquivalentTo(written.Select(e => e.Id));
        }

        // ── ChatDto.SaveHistory ──────────────────────────────────────────────────

        [Fact]
        public void ChatDto_SaveHistory_DefaultsFalse()
        {
            new ChatDto { Name = "x", Username = "", Type = "" }
                .SaveHistory.Should().BeFalse(
                    because: "history tracking must be opt-in");
        }

        [Fact]
        public void ChatDto_SaveHistory_CanBeEnabled()
        {
            var chat = new ChatDto { Name = "x", Username = "", Type = "", SaveHistory = true };
            chat.SaveHistory.Should().BeTrue();
        }

        // ── HistoryEntry model ───────────────────────────────────────────────────

        [Fact]
        public void HistoryEntry_TextOnly_HasNullMediaFields()
        {
            var entry = new HistoryEntry { Id = 1, Text = "Hello" };
            entry.MediaType.Should().BeNull();
            entry.FileName.Should().BeNull();
        }

        [Fact]
        public void HistoryEntry_Serialization_NullFieldsOmitted()
        {
            var entry = new HistoryEntry { Id = 3, Text = "hi" };
            var json  = JsonSerializer.Serialize(entry,
                new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                    PropertyNamingPolicy   = JsonNamingPolicy.CamelCase
                });

            json.Should().NotContain("mediaType",
                because: "null fields must be omitted in the compact JSONL output");
            json.Should().NotContain("fileName");
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static HistoryEntry MakeEntry(int id, string text) => new()
        {
            Id   = id,
            Date = DateTimeOffset.UtcNow,
            Text = text,
        };
    }
}
