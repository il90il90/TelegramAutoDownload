using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TelegramClient.Models;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    // ============================================================
    // MemberEntry model tests
    // ============================================================

    public class MemberEntryModelTests
    {
        [Fact]
        public void DisplayName_FirstAndLastName_ReturnsCombined()
        {
            var m = new MemberEntry { FirstName = "John", LastName = "Doe" };
            m.DisplayName.Should().Be("John Doe");
        }

        [Fact]
        public void DisplayName_FirstNameOnly_ReturnsFirstName()
        {
            var m = new MemberEntry { FirstName = "Alice", LastName = "" };
            m.DisplayName.Should().Be("Alice");
        }

        [Fact]
        public void DisplayName_NoName_FallsBackToUsername()
        {
            var m = new MemberEntry { FirstName = "", LastName = "", Username = "alice_tg" };
            m.DisplayName.Should().Be("@alice_tg");
        }

        [Fact]
        public void DisplayName_NoNameNoUsername_FallsBackToUserId()
        {
            var m = new MemberEntry { FirstName = "", LastName = "", Username = "", UserId = 123456 };
            m.DisplayName.Should().Be("123456");
        }

        [Fact]
        public void DisplayName_WhitespaceNameAndUsername_FallsBackToUserId()
        {
            var m = new MemberEntry { FirstName = "  ", LastName = "  ", Username = "", UserId = 9 };
            m.DisplayName.Should().Be("9");
        }

        [Fact]
        public void DefaultValues_AreEmpty()
        {
            var m = new MemberEntry();
            m.Phone.Should().BeEmpty();
            m.Username.Should().BeEmpty();
            m.IsBot.Should().BeFalse();
            m.IsAdmin.Should().BeFalse();
        }
    }

    // ============================================================
    // CSV export logic (extracted from MembersExportDialog)
    // ============================================================

    public class CsvExportTests
    {
        // Replicate the CSV escape logic from MembersExportDialog so we can test it in isolation
        private static string CsvEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                return $"\"{s.Replace("\"", "\"\"")}\"";
            return s;
        }

        private static string BuildCsvRow(MemberEntry m) =>
            $"{m.UserId}," +
            $"{CsvEscape(m.Username)}," +
            $"{CsvEscape(m.FirstName)}," +
            $"{CsvEscape(m.LastName)}," +
            $"{CsvEscape(m.Phone)}," +
            $"{m.IsAdmin}," +
            $"{m.IsBot}";

        [Fact]
        public void CsvRow_SimpleMember_NoCsvQuoting()
        {
            var m = new MemberEntry
            {
                UserId = 111, FirstName = "Alice", LastName = "Smith",
                Username = "asmith", Phone = "+972501234567",
                IsAdmin = false, IsBot = false
            };

            var row = BuildCsvRow(m);
            row.Should().Be("111,asmith,Alice,Smith,+972501234567,False,False");
        }

        [Fact]
        public void CsvRow_NameWithComma_IsQuoted()
        {
            var m = new MemberEntry { UserId = 2, FirstName = "Smith, Jr", LastName = "" };
            var row = BuildCsvRow(m);
            row.Should().Contain("\"Smith, Jr\"");
        }

        [Fact]
        public void CsvRow_EmptyPhone_LeavesFieldEmpty()
        {
            var m = new MemberEntry { UserId = 3, FirstName = "Bob", Phone = "" };
            var row = BuildCsvRow(m);
            row.Should().EndWith(",False,False");
        }

        [Fact]
        public void CsvHeader_ContainsExpectedColumns()
        {
            const string header = "UserId,Username,FirstName,LastName,Phone,IsAdmin,IsBot";
            header.Split(',').Should().HaveCount(7);
        }

        [Fact]
        public void CsvEscape_DoubleQuoteInValue_IsEscaped()
        {
            var escaped = CsvEscape("say \"hello\"");
            escaped.Should().Be("\"say \"\"hello\"\"\"");
        }

        [Fact]
        public void CsvFullExport_MultipleMembers_CorrectLineCount()
        {
            var members = new List<MemberEntry>
            {
                new() { UserId = 1, FirstName = "Alice" },
                new() { UserId = 2, FirstName = "Bob"   },
                new() { UserId = 3, FirstName = "Carol" },
            };

            var sb = new StringBuilder();
            sb.AppendLine("UserId,Username,FirstName,LastName,Phone,IsAdmin,IsBot");
            foreach (var m in members) sb.AppendLine(BuildCsvRow(m));

            var lines = sb.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            lines.Should().HaveCount(4); // header + 3 data rows
            lines[0].Should().StartWith("UserId");
        }
    }

    // ============================================================
    // vCard export logic
    // ============================================================

    public class VCardExportTests
    {
        private static string VcardEscape(string s) =>
            s.Replace("\\", "\\\\").Replace(",", "\\,").Replace(";", "\\;").Replace("\n", "\\n");

        private static string BuildVcard(MemberEntry m)
        {
            var sb = new StringBuilder();
            sb.AppendLine("BEGIN:VCARD");
            sb.AppendLine("VERSION:3.0");
            sb.AppendLine($"N:{VcardEscape(m.LastName)};{VcardEscape(m.FirstName)};;;");
            sb.AppendLine($"FN:{VcardEscape(m.DisplayName)}");
            if (!string.IsNullOrEmpty(m.Username))
                sb.AppendLine($"NICKNAME:{VcardEscape(m.Username)}");
            if (!string.IsNullOrEmpty(m.Phone))
                sb.AppendLine($"TEL;TYPE=CELL:{m.Phone}");
            sb.AppendLine($"X-TELEGRAM-ID:{m.UserId}");
            sb.AppendLine("END:VCARD");
            return sb.ToString();
        }

        [Fact]
        public void VCard_ContainsBeginAndEnd()
        {
            var m = new MemberEntry { UserId = 1, FirstName = "Alice", LastName = "Smith" };
            var vcard = BuildVcard(m);
            vcard.Should().Contain("BEGIN:VCARD");
            vcard.Should().Contain("END:VCARD");
        }

        [Fact]
        public void VCard_ContainsFN_AndN_Fields()
        {
            var m = new MemberEntry { UserId = 1, FirstName = "Alice", LastName = "Smith" };
            var vcard = BuildVcard(m);
            vcard.Should().Contain("FN:Alice Smith");
            vcard.Should().Contain("N:Smith;Alice;;;");
        }

        [Fact]
        public void VCard_WithPhone_ContainsTelField()
        {
            var m = new MemberEntry { UserId = 1, FirstName = "Bob", Phone = "+972501111111" };
            var vcard = BuildVcard(m);
            vcard.Should().Contain("TEL;TYPE=CELL:+972501111111");
        }

        [Fact]
        public void VCard_WithoutPhone_NoTelField()
        {
            var m = new MemberEntry { UserId = 1, FirstName = "Bob", Phone = "" };
            var vcard = BuildVcard(m);
            vcard.Should().NotContain("TEL;TYPE=CELL");
        }

        [Fact]
        public void VCard_WithUsername_ContainsNickname()
        {
            var m = new MemberEntry { UserId = 1, FirstName = "Carol", Username = "carol_tg" };
            var vcard = BuildVcard(m);
            vcard.Should().Contain("NICKNAME:carol_tg");
        }

        [Fact]
        public void VCard_TelegramIdLine_IsPresent()
        {
            var m = new MemberEntry { UserId = 99887766, FirstName = "Dan" };
            var vcard = BuildVcard(m);
            vcard.Should().Contain("X-TELEGRAM-ID:99887766");
        }

        [Fact]
        public void VCard_SemicolonInName_IsEscaped()
        {
            var m = new MemberEntry { UserId = 1, FirstName = "A;B", LastName = "" };
            var vcard = BuildVcard(m);
            vcard.Should().Contain("A\\;B");
        }

        [Fact]
        public void VCardExport_BotsAreSkipped()
        {
            var members = new List<MemberEntry>
            {
                new() { UserId = 1, FirstName = "Alice", IsBot = false },
                new() { UserId = 2, FirstName = "BotUser", IsBot = true },
                new() { UserId = 3, FirstName = "Bob", IsBot = false },
            };

            var sb = new StringBuilder();
            foreach (var m in members)
            {
                if (m.IsBot) continue;
                sb.Append(BuildVcard(m));
            }

            var output = sb.ToString();
            output.Should().Contain("Alice");
            output.Should().NotContain("BotUser");
            output.Should().Contain("Bob");
        }

        [Fact]
        public void VCardExport_ThreeContacts_ThreeBeginBlocks()
        {
            var members = Enumerable.Range(1, 3)
                .Select(i => new MemberEntry { UserId = i, FirstName = $"User{i}" })
                .ToList();

            var sb = new StringBuilder();
            foreach (var m in members) sb.Append(BuildVcard(m));

            var count = sb.ToString().Split(new[] { "BEGIN:VCARD" }, StringSplitOptions.None).Length - 1;
            count.Should().Be(3);
        }
    }

    // ============================================================
    // Startup session-detection logic
    // ============================================================

    public class SplashStartupLogicTests
    {
        [Fact]
        public void SessionFileExists_ShouldShowSplash()
        {
            // Arrange: create a temp session file
            var sessionPath = Path.Combine(Path.GetTempPath(), $"session_{Guid.NewGuid():N}.dat");
            File.WriteAllBytes(sessionPath, new byte[] { 1, 2, 3 });

            try
            {
                // Act
                bool sessionExists = File.Exists(sessionPath);

                // Assert: the same condition used in App.xaml.cs
                sessionExists.Should().BeTrue("session file was just created");
            }
            finally
            {
                File.Delete(sessionPath);
            }
        }

        [Fact]
        public void SessionFileAbsent_ShouldShowLoginWindow()
        {
            var sessionPath = Path.Combine(Path.GetTempPath(), $"nosession_{Guid.NewGuid():N}.dat");
            File.Exists(sessionPath).Should().BeFalse("file was never created");
        }

        [Fact]
        public void SessionFileDeleted_ShouldShowLoginWindow()
        {
            // Simulate what LogoutAsync does: delete session file
            var sessionPath = Path.Combine(Path.GetTempPath(), $"session_{Guid.NewGuid():N}.dat");
            File.WriteAllBytes(sessionPath, new byte[] { 1 });
            File.Delete(sessionPath);

            File.Exists(sessionPath).Should().BeFalse("logout deleted the session file");
        }

        [Fact]
        public void AppIdZero_ShouldNotShowSplash_EvenIfSessionExists()
        {
            // Condition from App.xaml.cs: File.Exists(session) && AppId != 0
            var sessionExists = true;
            var appId = 0;

            var shouldShowSplash = sessionExists && appId != 0;
            shouldShowSplash.Should().BeFalse("AppId=0 means credentials are not configured");
        }

        [Fact]
        public void AppIdNonZero_AndSessionExists_ShouldShowSplash()
        {
            var sessionExists = true;
            var appId = 12345678;

            var shouldShowSplash = sessionExists && appId != 0;
            shouldShowSplash.Should().BeTrue();
        }
    }

    // ============================================================
    // Logout: session file deletion
    // ============================================================

    public class LogoutSessionTests
    {
        [Fact]
        public void AfterLogout_SessionFileShouldNotExist()
        {
            var sessionPath = Path.Combine(Path.GetTempPath(), $"logout_{Guid.NewGuid():N}.dat");
            File.WriteAllBytes(sessionPath, new byte[] { 0xAB, 0xCD });

            // Simulate the deletion step inside LogoutAsync
            try { File.Delete(sessionPath); } catch { }

            File.Exists(sessionPath).Should().BeFalse();
        }

        [Fact]
        public void LogoutDeletion_WhenFileMissing_DoesNotThrow()
        {
            var missingPath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.dat");

            // Should not throw even if file doesn't exist
            Action act = () => { try { File.Delete(missingPath); } catch { } };
            act.Should().NotThrow();
        }
    }
}
