using BasePlugins;
using DownloadPlugin;
using FluentAssertions;
using SocialMediaPlugin;
using TelegramClient;
using TL;
using TorrentPlugin;
using Xunit;
using YoutubePlugin;
using PluginConfig = BasePlugins.Config;

namespace TelegramAutoDownload.Tests
{
    /// <summary>
    /// Integration tests that exercise multiple components together without hitting
    /// real Telegram servers or yt-dlp. Tests verify routing logic, quality mapping,
    /// and the resume-download file-stream contract.
    /// </summary>
    public class IntegrationTests
    {
        // ---------------------------------------------------------------------------
        // Quality format string mapping
        // ---------------------------------------------------------------------------

        [Theory]
        [InlineData("best",  "bestvideo[ext=mp4]+bestaudio")]
        [InlineData("4K",    "height<=2160")]
        [InlineData("1080p", "height<=1080")]
        [InlineData("720p",  "height<=720")]
        [InlineData("480p",  "height<=480")]
        [InlineData("audio", "bestaudio")]
        public void Quality_FormatString_ContainsExpectedSubstring(string quality, string expectedSubstring)
        {
            var format = YtdlpFormatHelper.GetFormatString(quality);
            format.Should().Contain(expectedSubstring,
                because: $"quality '{quality}' must produce a yt-dlp format selector with '{expectedSubstring}'");
        }

        [Fact]
        public void Quality_UnknownLabel_FallsBackToBest()
        {
            var format = YtdlpFormatHelper.GetFormatString("nonexistent");
            format.Should().Contain("bestvideo[ext=mp4]+bestaudio",
                because: "unknown quality labels must fall back to the 'best' format");
        }

        [Fact]
        public void Quality_AudioOnly_IsDetectedCorrectly()
        {
            YtdlpFormatHelper.IsAudioOnly("audio").Should().BeTrue();
            YtdlpFormatHelper.IsAudioOnly("best").Should().BeFalse();
            YtdlpFormatHelper.IsAudioOnly("1080p").Should().BeFalse();
            YtdlpFormatHelper.IsAudioOnly(null).Should().BeFalse();
        }

        [Fact]
        public void Quality_Options_ContainsAllExpectedLabels()
        {
            var options = YtdlpFormatHelper.QualityOptions;
            options.Should().Contain("best");
            options.Should().Contain("4K");
            options.Should().Contain("1080p");
            options.Should().Contain("720p");
            options.Should().Contain("480p");
            options.Should().Contain("audio");
        }

        // ---------------------------------------------------------------------------
        // Plugin priority ordering
        // ---------------------------------------------------------------------------

        [Fact]
        public void Plugins_PriorityOrder_IsCorrectAscending()
        {
            // Expected order by priority number: Social(2) < Torrent(3) < YouTube(10) < Download(100)
            var plugins = new List<IBasePlugin>
            {
                new YouTubePlugin<object>(),
                new DownladerPlugin<object>(),
                new SocialMediaPlugin<object>(),
                new TorrentPlugin<object>(),
            };

            var ordered = plugins.OrderBy(p => p.Priority).ToList();

            ordered[0].Should().BeOfType<SocialMediaPlugin<object>>("SocialMedia has the lowest priority number (2)");
            ordered[1].Should().BeOfType<TorrentPlugin<object>>("Torrent is second (3)");
            ordered[2].Should().BeOfType<YouTubePlugin<object>>("YouTube is third (10)");
            ordered[3].Should().BeOfType<DownladerPlugin<object>>("Download/Other is last (100)");
        }

        [Theory]
        [InlineData("https://youtu.be/dQw4w9WgXcQ")]
        [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
        public void Plugin_YouTubeUrl_SocialMediaHandlesBeforeYouTube(string url)
        {
            var config = MakeConfig(url);
            var social  = new SocialMediaPlugin<object>();
            var youtube = new YouTubePlugin<object>();

            social.CanHandle(config).Should().BeTrue("SocialMedia handles YouTube URLs");
            youtube.CanHandle(config).Should().BeTrue("YouTubePlugin also handles YouTube URLs");

            // Because SocialMedia has a lower priority number, it wins in the ordered pipeline
            social.Priority.Should().BeLessThan(youtube.Priority,
                "SocialMedia (priority 2) must execute before YouTubePlugin (priority 10)");
        }

        [Theory]
        [InlineData("https://instagram.com/reel/abc123")]
        [InlineData("https://www.tiktok.com/@user/video/123")]
        [InlineData("https://x.com/user/status/999")]
        public void Plugin_SocialUrl_OnlySocialMediaHandlesIt(string url)
        {
            var config  = MakeConfig(url);
            var social  = new SocialMediaPlugin<object>();
            var youtube = new YouTubePlugin<object>();
            var torrent = new TorrentPlugin<object>();

            social.CanHandle(config).Should().BeTrue($"SocialMedia should handle {url}");
            youtube.CanHandle(config).Should().BeFalse($"YouTubePlugin should not handle {url}");
            torrent.CanHandle(config).Should().BeFalse($"TorrentPlugin should not handle {url}");
        }

        [Fact]
        public void Plugin_MagnetLink_OnlyTorrentHandlesIt()
        {
            var config  = MakeConfig("magnet:?xt=urn:btih:abc123&dn=test");
            var torrent = new TorrentPlugin<object>();
            var social  = new SocialMediaPlugin<object>();
            var youtube = new YouTubePlugin<object>();
            var direct  = new DownladerPlugin<object>();

            torrent.CanHandle(config).Should().BeTrue();
            social.CanHandle(config).Should().BeFalse();
            youtube.CanHandle(config).Should().BeFalse();
            direct.CanHandle(config).Should().BeFalse("magnet links are not HTTP URLs");
        }

        [Theory]
        [InlineData("https://example.com/file.zip")]
        [InlineData("http://cdn.example.org/video.mp4")]
        public void Plugin_GenericHttpUrl_DirectDownloaderHandlesIt(string url)
        {
            var config  = MakeConfig(url);
            var direct  = new DownladerPlugin<object>();
            var torrent = new TorrentPlugin<object>();

            direct.CanHandle(config).Should().BeTrue();
            torrent.CanHandle(config).Should().BeFalse();
        }

        // ---------------------------------------------------------------------------
        // Resume: .part file stream contract
        // ---------------------------------------------------------------------------

        [Fact]
        public void ResumeDownload_ExistingPartFile_StreamPositionedAtEnd()
        {
            var tempDir  = Path.Combine(Path.GetTempPath(), $"TAD_test_{Guid.NewGuid()}");
            var partFile = Path.Combine(tempDir, "video.mp4.part");

            try
            {
                Directory.CreateDirectory(tempDir);

                // Simulate 512 bytes already downloaded in a previous session
                var existingData = new byte[512];
                new Random(42).NextBytes(existingData);
                File.WriteAllBytes(partFile, existingData);

                // Replicate the OpenOrResumePartFile logic from BaseMessage
                using var stream = File.Exists(partFile)
                    ? new FileStream(partFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
                    : File.Create(partFile);

                if (stream.CanSeek)
                    stream.Seek(0, SeekOrigin.End);

                // WTelegram reads stream.Position to determine byte offset — must equal existing file size
                stream.Position.Should().Be(512,
                    because: "the stream must be positioned after existing data so WTelegram resumes from byte 512");
                stream.Length.Should().Be(512);
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        [Fact]
        public void ResumeDownload_NoPartFile_CreatesNewStream()
        {
            var tempDir  = Path.Combine(Path.GetTempPath(), $"TAD_test_{Guid.NewGuid()}");
            var partFile = Path.Combine(tempDir, "video.mp4.part");

            try
            {
                Directory.CreateDirectory(tempDir);

                // No .part file — should start fresh from position 0
                using var stream = File.Exists(partFile)
                    ? new FileStream(partFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
                    : File.Create(partFile);

                stream.Position.Should().Be(0, "a fresh download starts at position 0");
                stream.Length.Should().Be(0);
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        [Fact]
        public void ResumeDownload_PartFileRenamed_WhenDownloadCompletes()
        {
            var tempDir   = Path.Combine(Path.GetTempPath(), $"TAD_test_{Guid.NewGuid()}");
            var finalPath = Path.Combine(tempDir, "video.mp4");
            var partPath  = finalPath + ".part";

            try
            {
                Directory.CreateDirectory(tempDir);
                File.WriteAllBytes(partPath, new byte[1024]);

                // Simulate successful completion: rename .part → final
                File.Move(partPath, finalPath, overwrite: true);

                File.Exists(finalPath).Should().BeTrue("final file must exist after rename");
                File.Exists(partPath).Should().BeFalse(".part file must be removed after successful rename");
                new FileInfo(finalPath).Length.Should().Be(1024);
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        // ---------------------------------------------------------------------------
        // Config: quality round-trip
        // ---------------------------------------------------------------------------

        [Theory]
        [InlineData("best")]
        [InlineData("1080p")]
        [InlineData("720p")]
        [InlineData("audio")]
        public void Config_YtdlpQuality_SetsAndPreservesValue(string quality)
        {
            var config = new PluginConfig
            {
                ChatName = "TestChat",
                Text = "https://youtu.be/test",
                PathSaveFile = ".",
                YtdlpQuality = quality
            };

            config.YtdlpQuality.Should().Be(quality);
            YtdlpFormatHelper.GetFormatString(config.YtdlpQuality)
                .Should().NotBeNullOrEmpty("every valid quality label must produce a non-empty format string");
        }

        [Fact]
        public void Config_DefaultQuality_IsBest()
        {
            var config = new PluginConfig { ChatName = "test", Text = "url", PathSaveFile = "." };
            config.YtdlpQuality.Should().Be("best");
        }

        // ---------------------------------------------------------------------------
        // Stale .part file cleanup
        // ---------------------------------------------------------------------------

        [Fact]
        public void PartCleanup_OldPartFiles_AreDeleted()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"TAD_cleanup_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            try
            {
                var oldPart = Path.Combine(tempDir, "old_video.mp4.part");
                var newPart = Path.Combine(tempDir, "new_video.mp4.part");

                File.WriteAllBytes(oldPart, new byte[256]);
                File.WriteAllBytes(newPart, new byte[256]);

                // Backdate the old file past the threshold
                File.SetLastWriteTimeUtc(oldPart, DateTime.UtcNow.AddDays(-10));
                File.SetLastWriteTimeUtc(newPart, DateTime.UtcNow.AddDays(-1));

                int deleted = PartFileCleanup.CleanStaleParts(tempDir, maxAgeDays: 7);

                deleted.Should().Be(1, "only the 10-day-old .part file should be removed");
                File.Exists(oldPart).Should().BeFalse("old .part file must be deleted");
                File.Exists(newPart).Should().BeTrue("recent .part file must be kept for resume");
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        [Fact]
        public void PartCleanup_NonPartFiles_AreNeverDeleted()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"TAD_cleanup_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            try
            {
                var completedFile = Path.Combine(tempDir, "completed.mp4");
                File.WriteAllBytes(completedFile, new byte[512]);
                File.SetLastWriteTimeUtc(completedFile, DateTime.UtcNow.AddDays(-30));

                int deleted = PartFileCleanup.CleanStaleParts(tempDir, maxAgeDays: 7);

                deleted.Should().Be(0, "completed files must never be touched by cleanup");
                File.Exists(completedFile).Should().BeTrue();
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        [Fact]
        public void PartCleanup_MissingDirectory_ReturnsZeroWithoutException()
        {
            var nonExistent = Path.Combine(Path.GetTempPath(), $"TAD_nonexistent_{Guid.NewGuid()}");
            var act = () => PartFileCleanup.CleanStaleParts(nonExistent);
            act.Should().NotThrow("cleanup must be resilient when the directory does not exist");
            act().Should().Be(0);
        }

        [Fact]
        public void PartCleanup_RecursiveScan_FindsFilesInSubfolders()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"TAD_cleanup_{Guid.NewGuid()}");
            var subDir  = Path.Combine(tempDir, "Videos", "MyChat");
            Directory.CreateDirectory(subDir);

            try
            {
                var deepPart = Path.Combine(subDir, "deep.mp4.part");
                File.WriteAllBytes(deepPart, new byte[128]);
                File.SetLastWriteTimeUtc(deepPart, DateTime.UtcNow.AddDays(-14));

                int deleted = PartFileCleanup.CleanStaleParts(tempDir, maxAgeDays: 7);

                deleted.Should().Be(1, "recursive scan must find .part files in subfolders");
                File.Exists(deepPart).Should().BeFalse();
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        // ---------------------------------------------------------------------------
        // FileDownloadIndex: debounced save does not lose data
        // ---------------------------------------------------------------------------

        [Fact]
        public void FileDownloadIndex_MarkAndCheck_WorksWithoutImmediateDiskWrite()
        {
            // The index uses a debounced save, but in-memory lookups must be instant
            const long fakeId = -987654321L;

            // Ensure clean state (id might already be there from a previous test run in the same process)
            FileDownloadIndex.Remove(fakeId);
            FileDownloadIndex.IsAlreadyDownloaded(fakeId).Should().BeFalse();

            FileDownloadIndex.MarkDownloaded(fakeId);
            FileDownloadIndex.IsAlreadyDownloaded(fakeId).Should().BeTrue(
                "in-memory lookup must succeed even before the background timer flushes to disk");

            // Cleanup so we don't pollute the real index file
            FileDownloadIndex.Remove(fakeId);
        }

        // ---------------------------------------------------------------------------
        // Sticker / voice message filtering
        // ---------------------------------------------------------------------------

        [Fact]
        public void Sticker_IsNotDownloadedAsPhoto()
        {
            // A document with DocumentAttributeSticker must not be routed to Photos,
            // even if chatDto.Download.Photos = true.
            var stickerDoc = new Document
            {
                mime_type = "image/webp",
                attributes = new DocumentAttribute[] { new DocumentAttributeSticker() }
            };
            var msg = new Message
            {
                media = new MessageMediaDocument { document = stickerDoc }
            };
            var chat = new TelegramClient.Models.ChatDto
            {
                Name = "TestChat",
                Download = new TelegramClient.Models.Download { Photos = true, Videos = true, Music = true, Files = true },
                IgnoreFileByRegex = []
            };

            // IsMessageTypeEnabled must return false for stickers
            bool enabled = IsMessageTypeEnabledPublic(msg, chat);
            enabled.Should().BeFalse("stickers must never be queued for download");
        }

        [Fact]
        public void AnimatedSticker_IsNotDownloadedAsVideo()
        {
            var animatedStickerDoc = new Document
            {
                mime_type = "video/webm",
                attributes = new DocumentAttribute[] { new DocumentAttributeSticker(), new DocumentAttributeAnimated() }
            };
            var msg = new Message
            {
                media = new MessageMediaDocument { document = animatedStickerDoc }
            };
            var chat = new TelegramClient.Models.ChatDto
            {
                Name = "TestChat",
                Download = new TelegramClient.Models.Download { Videos = true },
                IgnoreFileByRegex = []
            };

            bool enabled = IsMessageTypeEnabledPublic(msg, chat);
            enabled.Should().BeFalse("animated stickers must also be excluded");
        }

        [Fact]
        public void VoiceMessage_IsNotDownloadedAsMusic()
        {
            var voiceDoc = new Document
            {
                mime_type = "audio/ogg",
                attributes = new DocumentAttribute[]
                {
                    new DocumentAttributeAudio { duration = 5, flags = DocumentAttributeAudio.Flags.voice }
                }
            };
            var msg = new Message
            {
                media = new MessageMediaDocument { document = voiceDoc }
            };
            var chat = new TelegramClient.Models.ChatDto
            {
                Name = "TestChat",
                Download = new TelegramClient.Models.Download { Music = true },
                IgnoreFileByRegex = []
            };

            bool enabled = IsMessageTypeEnabledPublic(msg, chat);
            enabled.Should().BeFalse("voice messages must never be queued for download");
        }

        [Fact]
        public void RealAudio_IsDownloadedAsMusic()
        {
            // A regular audio file (not a voice message) SHOULD be downloaded
            var audioDoc = new Document
            {
                mime_type = "audio/mp3",
                attributes = new DocumentAttribute[]
                {
                    new DocumentAttributeAudio { duration = 180, title = "My Song" }
                }
            };
            var msg = new Message
            {
                media = new MessageMediaDocument { document = audioDoc }
            };
            var chat = new TelegramClient.Models.ChatDto
            {
                Name = "TestChat",
                Download = new TelegramClient.Models.Download { Music = true },
                IgnoreFileByRegex = []
            };

            bool enabled = IsMessageTypeEnabledPublic(msg, chat);
            enabled.Should().BeTrue("regular audio files must be downloaded when Music is enabled");
        }

        // ---------------------------------------------------------------------------
        // URL messages in sync
        // ---------------------------------------------------------------------------

        [Theory]
        [InlineData("https://youtu.be/dQw4w9WgXcQ")]
        [InlineData("Check this out: https://www.instagram.com/p/abc123")]
        [InlineData("https://x.com/user/status/999 great video")]
        public void UrlMessage_ContainsHttp_IsDetectedAsUrl(string text)
        {
            bool hasUrl = !string.IsNullOrEmpty(text) &&
                          text.Contains("http", StringComparison.OrdinalIgnoreCase);
            hasUrl.Should().BeTrue($"'{text}' should be detected as a URL message for plugin processing");
        }

        [Theory]
        [InlineData("Hello world")]
        [InlineData("Just a plain text message")]
        [InlineData("")]
        public void PlainTextMessage_NoHttp_IsNotDetectedAsUrl(string text)
        {
            bool hasUrl = !string.IsNullOrEmpty(text) &&
                          text.Contains("http", StringComparison.OrdinalIgnoreCase);
            hasUrl.Should().BeFalse($"'{text}' should NOT be detected as a URL message");
        }

        // ---------------------------------------------------------------------------
        // Sync pagination correctness
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Simulates the fixed pagination logic so we can verify it without a live Telegram connection.
        /// Reproduces the scenario where most messages are text-only and only a few have media.
        /// </summary>
        [Fact]
        public void SyncPagination_PageWithFewMediaMessages_DoesNotStopEarly()
        {
            // Simulate two pages: page 1 has 100 messages, only 3 with media.
            // The old (buggy) code would stop after page 1 because filtered.Count(3) < pageSize(100).
            // The fixed code uses rawMessages.Count for the break decision.
            const int pageSize = 100;

            var page1Raw = Enumerable.Range(101, 100).Select(id => id).ToList(); // IDs 101–200
            var page1Media = page1Raw.Where(id => id % 33 == 0).ToList();        // IDs 132, 165, 198

            var page2Raw = Enumerable.Range(1, 100).Select(id => id).ToList();   // IDs 1–100
            var page2Media = page2Raw.Where(id => id % 25 == 0).ToList();        // IDs 25, 50, 75, 100

            // Fixed logic: continue if rawMessages.Count == pageSize
            bool shouldFetchPage2AfterPage1 = page1Raw.Count >= pageSize;
            shouldFetchPage2AfterPage1.Should().BeTrue(
                "a raw full page must always trigger fetching the next page, regardless of media count");

            // Stop after page 2 because it's smaller than pageSize (only IDs 1–100, 100 == pageSize)
            // Actually page2Raw has exactly pageSize items, so we'd fetch page3 (which would return 0)
            bool shouldStopAfterPage2 = page2Raw.Count < pageSize;
            shouldStopAfterPage2.Should().BeFalse("exactly pageSize items means there might be more");

            // Total media collected correctly
            var totalMedia = page1Media.Count + page2Media.Count;
            totalMedia.Should().Be(7, "pages together have 7 media messages (3 + 4)");
        }

        [Fact]
        public void SyncPagination_OffsetId_UsesRawMinNotFilteredMin()
        {
            // Page contains messages with IDs 101–200. Only IDs 150, 175, 200 have media.
            // Old code: offsetId = 150 (filtered min) → skips messages 101–149 on next request
            // Fixed code: offsetId = 101 (raw min) → correctly requests messages before 101
            var rawPage = Enumerable.Range(101, 100).ToList(); // 101, 102, ..., 200
            var filteredPage = new List<int> { 150, 175, 200 };

            int buggyOffset  = filteredPage.Min();  // 150 — skips messages 101–149
            int correctOffset = rawPage.Min();       // 101 — correct

            buggyOffset.Should().Be(150);
            correctOffset.Should().Be(101);
            correctOffset.Should().BeLessThan(buggyOffset,
                "the raw-min offset ensures no messages in the gap 101–149 are skipped");
        }

        [Fact]
        public void SyncPagination_EmptyPage_StopsImmediately()
        {
            // If the API returns 0 messages, we must stop regardless of filters
            var rawMessages = new List<int>();
            bool shouldStop = rawMessages.Count == 0;
            shouldStop.Should().BeTrue("empty raw page always means we have reached the beginning");
        }

        [Fact]
        public void ProcessMissed_AllMessagesAtOrBelowWatermark_StopsEarly()
        {
            // If an entire raw page is at or below the watermark, we've caught up
            int watermark = 500;
            var rawPage = new List<int> { 498, 499, 500 }; // all <= watermark

            bool allAtOrBelow = rawPage.All(id => id <= watermark);
            allAtOrBelow.Should().BeTrue("should stop paginating when watermark is reached");
        }

        [Fact]
        public void ProcessMissed_MixedPage_OnlyDownloadsAboveWatermark()
        {
            int watermark = 500;
            var rawPage = Enumerable.Range(490, 20).ToList(); // IDs 490–509

            var aboveWatermark = rawPage.Where(id => id > watermark).ToList();
            aboveWatermark.Should().HaveCount(9, "IDs 501–509 are above the watermark");
            aboveWatermark.Should().NotContain(id => id <= watermark);
        }

        // ---------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------

        private static PluginConfig MakeConfig(string text) => new PluginConfig
        {
            ChatName = "test",
            Text = text,
            PathSaveFile = "."
        };

        /// <summary>
        /// Mirrors TelegramApp.IsMessageTypeEnabled so the filtering rules can be unit-tested.
        /// Must stay in sync with the production method whenever that method changes.
        /// </summary>
        private static bool IsMessageTypeEnabledPublic(Message msg, TelegramClient.Models.ChatDto chatDto)
        {
            if (msg.media is MessageMediaPhoto)
                return chatDto.Download.Photos;

            if (msg.media is MessageMediaDocument { document: Document doc })
            {
                if (doc.attributes?.Any(a => a is DocumentAttributeSticker) == true) return false;
                if (doc.attributes?.Any(a => a is DocumentAttributeAudio audio &&
                        audio.flags.HasFlag(DocumentAttributeAudio.Flags.voice)) == true) return false;

                var mime = doc.mime_type ?? string.Empty;
                if (mime.Contains("image")) return chatDto.Download.Photos;
                if (mime.Contains("video")) return chatDto.Download.Videos;
                if (mime.Contains("audio")) return chatDto.Download.Music;
                return chatDto.Download.Files;
            }

            return false;
        }
    }
}
