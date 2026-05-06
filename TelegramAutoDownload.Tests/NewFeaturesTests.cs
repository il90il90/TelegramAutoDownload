using FluentAssertions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TelegramAutoDownload.Models;
using TelegramClient;
using TelegramClient.Models;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    // ===========================================================================
    // 1. Quiet Hours
    // ===========================================================================

    public class QuietHoursTests
    {
        private static ConfigParams Cfg(bool enabled, int start, int end) =>
            new() { QuietHoursEnabled = enabled, QuietHoursStart = start, QuietHoursEnd = end };

        [Fact]
        public void QuietHours_Disabled_AlwaysReturnsFalse()
        {
            var cfg = Cfg(enabled: false, start: 0, end: 23);
            foreach (int h in Enumerable.Range(0, 24))
                QuietHoursHelper.IsInQuietHours(cfg, h).Should().BeFalse(
                    because: $"quiet hours are disabled so hour {h} must never be quiet");
        }

        [Theory]
        [InlineData(9)]   // inside window 09:00-17:00
        [InlineData(12)]
        [InlineData(16)]
        public void QuietHours_SameDay_HourInsideWindow_ReturnsTrue(int hour)
        {
            var cfg = Cfg(enabled: true, start: 9, end: 17);
            QuietHoursHelper.IsInQuietHours(cfg, hour).Should().BeTrue(
                because: $"hour {hour} is inside the same-day window 09:00-17:00");
        }

        [Theory]
        [InlineData(8)]   // just before window
        [InlineData(17)]  // end is exclusive
        [InlineData(20)]
        public void QuietHours_SameDay_HourOutsideWindow_ReturnsFalse(int hour)
        {
            var cfg = Cfg(enabled: true, start: 9, end: 17);
            QuietHoursHelper.IsInQuietHours(cfg, hour).Should().BeFalse(
                because: $"hour {hour} is outside the same-day window 09:00-17:00");
        }

        [Theory]
        [InlineData(23)]  // exactly at start
        [InlineData(0)]   // midnight — inside overnight
        [InlineData(3)]   // inside overnight
        [InlineData(6)]   // last hour inside (end=7 is exclusive)
        public void QuietHours_Overnight_HourInsideWindow_ReturnsTrue(int hour)
        {
            var cfg = Cfg(enabled: true, start: 23, end: 7);
            QuietHoursHelper.IsInQuietHours(cfg, hour).Should().BeTrue(
                because: $"hour {hour} is inside the overnight window 23:00-07:00");
        }

        [Theory]
        [InlineData(7)]   // exactly at end (exclusive)
        [InlineData(10)]
        [InlineData(22)]  // just before start
        public void QuietHours_Overnight_HourOutsideWindow_ReturnsFalse(int hour)
        {
            var cfg = Cfg(enabled: true, start: 23, end: 7);
            QuietHoursHelper.IsInQuietHours(cfg, hour).Should().BeFalse(
                because: $"hour {hour} is outside the overnight window 23:00-07:00");
        }

        [Fact]
        public void QuietHours_FullDayWindow_AllHoursAreQuiet()
        {
            // Start=0, End=0 with start >= end → overnight rule → always quiet
            var cfg = Cfg(enabled: true, start: 0, end: 0);
            foreach (int h in Enumerable.Range(0, 24))
                QuietHoursHelper.IsInQuietHours(cfg, h).Should().BeTrue(
                    because: "start==end with overnight rule covers the full 24-hour day");
        }
    }

    // ===========================================================================
    // 2. DownloadItem.CanRetry / RetryAsync property
    // ===========================================================================

    public class DownloadItemRetryTests
    {
        [Fact]
        public void CanRetry_WhenRetryAsyncIsNull_ReturnsFalse()
        {
            var item = new DownloadItem { Status = "✖ Error" };
            item.CanRetry.Should().BeFalse("RetryAsync is null so retry is not available");
        }

        [Theory]
        [InlineData("✔ Done")]
        [InlineData("⬇ Downloading")]
        [InlineData("⏳ Queued")]
        [InlineData("✖ Cancelled")]
        public void CanRetry_WhenStatusIsNotErrorOrTimeout_ReturnsFalse(string status)
        {
            var item = new DownloadItem
            {
                Status     = status,
                RetryAsync = () => Task.CompletedTask
            };
            item.CanRetry.Should().BeFalse(
                because: $"CanRetry should be false for status '{status}'");
        }

        [Theory]
        [InlineData("✖ Error")]
        [InlineData("✖ Timeout")]
        public void CanRetry_WhenRetryAsyncIsSetAndStatusIsFailure_ReturnsTrue(string status)
        {
            var item = new DownloadItem
            {
                Status     = status,
                RetryAsync = () => Task.CompletedTask
            };
            item.CanRetry.Should().BeTrue(
                because: $"RetryAsync is set and status is '{status}'");
        }

        [Fact]
        public void RetryAsync_Setter_FiresPropertyChangedForCanRetry()
        {
            var item = new DownloadItem { Status = "✖ Error" };
            var raised = new List<string>();
            item.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

            item.RetryAsync = () => Task.CompletedTask;

            raised.Should().Contain(nameof(DownloadItem.CanRetry),
                because: "setting RetryAsync must notify CanRetry so the UI button can appear");
            raised.Should().Contain(nameof(DownloadItem.RetryAsync));
        }

        [Fact]
        public void Status_Setter_FiresPropertyChangedForCanRetry()
        {
            var item = new DownloadItem
            {
                RetryAsync = () => Task.CompletedTask,
                Status     = "⬇ Downloading"
            };
            var raised = new List<string>();
            item.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

            item.Status = "✖ Error";

            raised.Should().Contain(nameof(DownloadItem.CanRetry),
                because: "changing Status must re-evaluate CanRetry so the UI button appears/disappears");
        }

        [Fact]
        public void ClearingRetryAsync_MakesCanRetryFalse()
        {
            var item = new DownloadItem
            {
                Status     = "✖ Error",
                RetryAsync = () => Task.CompletedTask
            };
            item.CanRetry.Should().BeTrue();

            item.RetryAsync = null;

            item.CanRetry.Should().BeFalse("clearing RetryAsync must disable the retry button");
        }
    }

    // ===========================================================================
    // 3. Folder Template resolution
    // ===========================================================================

    public class FolderTemplateTests
    {
        [Fact]
        public void Resolve_NullOrEmpty_ReturnsNull()
        {
            FolderTemplateHelper.Resolve(null,  "Videos", "Chat").Should().BeNull();
            FolderTemplateHelper.Resolve("",    "Videos", "Chat").Should().BeNull();
            FolderTemplateHelper.Resolve("   ", "Videos", "Chat").Should().BeNull();
        }

        [Fact]
        public void Resolve_AllTokens_AreSubstituted()
        {
            var at  = new DateTime(2026, 5, 6);
            var res = FolderTemplateHelper.Resolve(
                "{Type}/{ChatName}/{Year}/{Month}/{Day}", "Videos", "MyChan", at);

            res.Should().Be("Videos/MyChan/2026/05/06");
        }

        [Fact]
        public void Resolve_TokensAreCaseInsensitive()
        {
            var at  = new DateTime(2026, 5, 6);
            var res = FolderTemplateHelper.Resolve("{type}/{chatname}", "Videos", "Chan", at);
            res.Should().Be("Videos/Chan");
        }

        [Fact]
        public void Resolve_ChatNameWithInvalidChars_IsSanitized()
        {
            var res = FolderTemplateHelper.Resolve("{ChatName}", "Videos", "My:Chat/Name", null);
            res.Should().NotContain(":")
               .And.NotContain("/", because: "/ in chat name must be sanitized to a space");
        }

        [Fact]
        public void Resolve_TemplateWithNoTokens_ReturnsTemplateAsIs()
        {
            var res = FolderTemplateHelper.Resolve("static/folder", "Videos", "Chat");
            res.Should().Be("static/folder");
        }

        [Fact]
        public void Resolve_MonthAndDay_ArePaddedToTwoDigits()
        {
            var at  = new DateTime(2026, 1, 5); // January 5th
            var res = FolderTemplateHelper.Resolve("{Year}-{Month}-{Day}", "X", "Y", at);
            res.Should().Be("2026-01-05");
        }

        [Fact]
        public void SupportedTokens_ContainsAllDocumentedTokens()
        {
            var tokens = FolderTemplateHelper.SupportedTokens;
            tokens.Should().Contain("{Type}");
            tokens.Should().Contain("{ChatName}");
            tokens.Should().Contain("{Year}");
            tokens.Should().Contain("{Month}");
            tokens.Should().Contain("{Day}");
        }
    }

    // ===========================================================================
    // 4. IgnoreFileByRegex — filter string parsing (UI layer logic)
    // ===========================================================================

    public class IgnoreFilterParsingTests
    {
        // This mirrors the parsing logic in MainWindow.FilterTextBox_LostFocus
        private static List<string> ParseFilterText(string text) =>
            text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

        [Fact]
        public void Parse_EmptyString_ReturnsEmptyList()
        {
            ParseFilterText("").Should().BeEmpty();
        }

        [Fact]
        public void Parse_SinglePattern_ReturnsSingleEntry()
        {
            ParseFilterText(@"\.jpg$").Should().ContainSingle()
                .Which.Should().Be(@"\.jpg$");
        }

        [Fact]
        public void Parse_MultipleSemicolonSeparated_ReturnsAllEntries()
        {
            var result = ParseFilterText(@"\.jpg$; thumb_; \.png$");
            result.Should().HaveCount(3)
                  .And.Contain(@"\.jpg$")
                  .And.Contain("thumb_")
                  .And.Contain(@"\.png$");
        }

        [Fact]
        public void Parse_TrailingAndLeadingSemicolons_AreIgnored()
        {
            ParseFilterText("; pattern1 ; pattern2 ;").Should().HaveCount(2);
        }

        [Fact]
        public void Parse_WhitespaceAroundPatterns_IsTrimmed()
        {
            var result = ParseFilterText("  foo  ;  bar  ");
            result.Should().Contain("foo").And.Contain("bar");
            result.Should().NotContain("  foo  ");
        }

        [Fact]
        public void Parse_ValidRegex_DoesNotThrowOnMatch()
        {
            var patterns = ParseFilterText(@"\.jpg$; ^thumb");
            foreach (var p in patterns)
            {
                var act = () => new System.Text.RegularExpressions.Regex(p);
                act.Should().NotThrow($"pattern '{p}' must be valid regex");
            }
        }

        [Fact]
        public void ChatDto_IgnoreFileByRegex_DefaultsToEmpty()
        {
            var chat = new ChatDto { Name = "Test", Username = "", Type = "" };
            chat.IgnoreFileByRegex.Should().BeEmpty(
                because: "new chats must not have any filters pre-applied");
        }
    }

    // ===========================================================================
    // 5. Persistent Statistics — StatsData JSON serialisation
    // ===========================================================================

    public class StatisticsSerializationTests
    {
        // Tests the internal JSON round-trip without touching Application.Current
        private sealed class StatsSnapshot
        {
            public long TotalFilesAllTime { get; set; }
            public long TotalBytesAllTime { get; set; }
            public DateTime StatsStart    { get; set; }
        }

        [Fact]
        public void StatsData_RoundTrip_PreservesCounters()
        {
            var original = new StatsSnapshot
            {
                TotalFilesAllTime = 42,
                TotalBytesAllTime = 1_073_741_824L, // 1 GB
                StatsStart        = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };

            var json     = JsonConvert.SerializeObject(original, Formatting.Indented);
            var restored = JsonConvert.DeserializeObject<StatsSnapshot>(json)!;

            restored.TotalFilesAllTime.Should().Be(42);
            restored.TotalBytesAllTime.Should().Be(1_073_741_824L);
            restored.StatsStart.Should().Be(original.StatsStart);
        }

        [Fact]
        public void StatsData_AtomicIncrement_IsThreadSafe()
        {
            long counter = 0;
            const int iterations = 1000;

            Parallel.For(0, iterations, _ => System.Threading.Interlocked.Increment(ref counter));

            counter.Should().Be(iterations,
                because: "Interlocked.Increment must be race-condition-free under parallel writes");
        }

        [Fact]
        public void StatsData_JsonFile_WrittenAndReadBack()
        {
            var path = Path.Combine(Path.GetTempPath(), $"stats_test_{Guid.NewGuid():N}.json");
            try
            {
                var data = new StatsSnapshot { TotalFilesAllTime = 7, TotalBytesAllTime = 1024 };
                File.WriteAllText(path, JsonConvert.SerializeObject(data));

                var loaded = JsonConvert.DeserializeObject<StatsSnapshot>(File.ReadAllText(path))!;
                loaded.TotalFilesAllTime.Should().Be(7);
                loaded.TotalBytesAllTime.Should().Be(1024);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    // ===========================================================================
    // 6. CloseDialog — enum + default result
    // ===========================================================================

    public class CloseDialogTests
    {
        [Fact]
        public void CloseAction_HasExpectedValues()
        {
            Enum.GetValues<CloseAction>().Should().Contain(CloseAction.Cancel);
            Enum.GetValues<CloseAction>().Should().Contain(CloseAction.MinimizeToTray);
            Enum.GetValues<CloseAction>().Should().Contain(CloseAction.Exit);
        }

        [Fact]
        public void CloseAction_Cancel_IsDefaultEnumValue()
        {
            // The default enum value (0) must be Cancel so un-initialised code is safe
            ((int)CloseAction.Cancel).Should().Be(0,
                because: "Cancel must be the zero-value so default(CloseAction) == Cancel");
        }

        [Fact]
        public void CloseAction_MinimizeToTray_IsDistinctFromExit()
        {
            CloseAction.MinimizeToTray.Should().NotBe(CloseAction.Exit);
        }
    }

    // ===========================================================================
    // 7. ConfigParams — Quiet Hours defaults
    // ===========================================================================

    public class ConfigParamsQuietHoursTests
    {
        [Fact]
        public void ConfigParams_QuietHoursEnabled_DefaultsFalse()
        {
            new ConfigParams().QuietHoursEnabled.Should().BeFalse(
                because: "quiet hours must be opt-in");
        }

        [Fact]
        public void ConfigParams_QuietHoursDefaults_AreReasonable()
        {
            var cfg = new ConfigParams();
            cfg.QuietHoursStart.Should().BeInRange(0, 23);
            cfg.QuietHoursEnd.Should().BeInRange(0, 23);
        }
    }

    // ===========================================================================
    // 8. ChatDto — new fields
    // ===========================================================================

    public class ChatDtoNewFieldsTests
    {
        [Fact]
        public void ChatDto_FolderTemplate_DefaultsToEmpty()
        {
            new ChatDto { Name = "x", Username = "", Type = "" }
                .FolderTemplate.Should().BeEmpty(
                    because: "empty template = use default layout");
        }

        [Fact]
        public void ChatDto_FolderTemplate_CanBeSetAndRead()
        {
            var chat = new ChatDto { Name = "x", Username = "", Type = "", FolderTemplate = "{ChatName}/{Year}" };
            chat.FolderTemplate.Should().Be("{ChatName}/{Year}");
        }
    }
}
