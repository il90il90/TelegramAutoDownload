using FluentAssertions;
using Newtonsoft.Json;
using System;
using System.IO;
using TelegramAutoDownload.Models;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    /// <summary>
    /// Tests for ConfigFile I/O: save → read round-trip, auto-create on first run,
    /// and correct handling of missing or corrupt files.
    /// All tests use an isolated temp directory so they never touch the real config on disk.
    /// </summary>
    public class ConfigFilePersistenceTests : IDisposable
    {
        private readonly string _dir  = Path.Combine(Path.GetTempPath(), $"cfgtest_{Guid.NewGuid():N}");
        private string TempPath(string name = "config.txt") => Path.Combine(_dir, name);

        public ConfigFilePersistenceTests()
        {
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        // ---------------------------------------------------------------------------
        // Auto-create on first run
        // ---------------------------------------------------------------------------

        [Fact]
        public void Constructor_FileNotFound_CreatesFileWithDefaults()
        {
            var path = TempPath("auto_create.txt");
            File.Exists(path).Should().BeFalse("precondition: file must not exist yet");

            _ = new ConfigFile(path);

            File.Exists(path).Should().BeTrue("constructor must create the config file when absent");
            var content = File.ReadAllText(path);
            content.Should().Contain("{", "created file must contain valid JSON");
        }

        [Fact]
        public void Constructor_FileAlreadyExists_DoesNotOverwrite()
        {
            var path = TempPath("no_overwrite.txt");
            var original = new ConfigParams { AppId = 42 };
            File.WriteAllText(path, JsonConvert.SerializeObject(original, Formatting.Indented));

            _ = new ConfigFile(path); // must not touch the existing file

            var read = JsonConvert.DeserializeObject<ConfigParams>(File.ReadAllText(path));
            read!.AppId.Should().Be(42, "existing file must not be overwritten by constructor");
        }

        // ---------------------------------------------------------------------------
        // Save / Read round-trip
        // ---------------------------------------------------------------------------

        [Fact]
        public void SaveAndRead_AppId_IsPreserved()
        {
            var path = TempPath("round_trip_appid.txt");
            var cfg = new ConfigFile(path);
            var original = new ConfigParams { AppId = 12345678 };

            cfg.Save(original);
            var restored = cfg.Read();

            restored.AppId.Should().Be(12345678);
        }

        [Fact]
        public void SaveAndRead_ApiHash_IsPreserved()
        {
            var path = TempPath("round_trip_hash.txt");
            var cfg = new ConfigFile(path);
            var original = new ConfigParams { ApiHash = "abc123def456" };

            cfg.Save(original);

            cfg.Read().ApiHash.Should().Be("abc123def456");
        }

        [Fact]
        public void SaveAndRead_DownloadThreads_IsPreserved()
        {
            var path = TempPath("round_trip_threads.txt");
            var cfg = new ConfigFile(path);
            var original = new ConfigParams { DownloadThreads = 8 };

            cfg.Save(original);

            cfg.Read().DownloadThreads.Should().Be(8);
        }

        [Fact]
        public void SaveAndRead_DarkMode_IsPreserved()
        {
            var path = TempPath("round_trip_dark.txt");
            var cfg = new ConfigFile(path);
            var original = new ConfigParams { DarkMode = true };

            cfg.Save(original);

            cfg.Read().DarkMode.Should().BeTrue();
        }

        [Fact]
        public void SaveAndRead_ChatList_IsPreserved()
        {
            var path = TempPath("round_trip_chats.txt");
            var cfg = new ConfigFile(path);
            var original = new ConfigParams
            {
                Chats =
                [
                    new TelegramClient.Models.ChatDto { Name = "MyGroup", Id = 1001 },
                    new TelegramClient.Models.ChatDto { Name = "DMs",     Id = 1002 }
                ]
            };

            cfg.Save(original);
            var restored = cfg.Read();

            restored.Chats.Should().HaveCount(2);
            restored.Chats[0].Name.Should().Be("MyGroup");
            restored.Chats[1].Id.Should().Be(1002);
        }

        [Fact]
        public void SaveAndRead_YtdlpQuality_IsPreserved()
        {
            var path = TempPath("round_trip_quality.txt");
            var cfg = new ConfigFile(path);
            var chat = new TelegramClient.Models.ChatDto { Name = "CH", YtdlpQuality = "1080p" };
            var original = new ConfigParams { Chats = [chat] };

            cfg.Save(original);

            cfg.Read().Chats[0].YtdlpQuality.Should().Be("1080p");
        }

        [Fact]
        public void Save_WritesValidJson()
        {
            var path = TempPath("valid_json.txt");
            var cfg = new ConfigFile(path);
            cfg.Save(new ConfigParams { AppId = 99 });

            var json = File.ReadAllText(path);
            var act = () => JsonConvert.DeserializeObject<ConfigParams>(json);
            act.Should().NotThrow("Save must always produce valid, parseable JSON");
        }

        [Fact]
        public void Save_Overwrites_PreviousData()
        {
            var path = TempPath("overwrite.txt");
            var cfg = new ConfigFile(path);

            cfg.Save(new ConfigParams { AppId = 1 });
            cfg.Save(new ConfigParams { AppId = 2 });

            cfg.Read().AppId.Should().Be(2, "second Save must overwrite the first");
        }

        // ---------------------------------------------------------------------------
        // Default values on first run
        // ---------------------------------------------------------------------------

        [Fact]
        public void Read_NewFile_YtdlpQualityDefaultsToEmpty()
        {
            // A newly created file has an empty ConfigParams — no chats yet
            var path = TempPath("defaults.txt");
            var cfg = new ConfigFile(path);

            var result = cfg.Read();
            result.Chats.Should().BeEmpty("a brand-new config has no chats configured");
        }

        // ---------------------------------------------------------------------------
        // Corrupt file
        // ---------------------------------------------------------------------------

        [Fact]
        public void Read_CorruptJson_ThrowsJsonReaderException()
        {
            var path = TempPath("corrupt.txt");
            File.WriteAllText(path, "{ this is not json!! @@@");
            var cfg = new ConfigFile(path);

            var act = () => cfg.Read();
            act.Should().Throw<JsonReaderException>("corrupt JSON must surface as a parse error");
        }
    }
}
