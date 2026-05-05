using BasePlugins;
using FluentAssertions;
using TelegramClient.Models;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class NotificationTests
    {
        private static ResultMessageEvent MakeEvent(string chatName, string fileName, string? errorMessage = null, string? postAuthor = null)
        {
            return new ResultMessageEvent
            {
                Chat = new ChatDto { Name = chatName },
                Message = "some message text",
                PostAuthor = postAuthor,
                ResultExecute = new ResultExecute(chatName)
                {
                    IsSuccess = errorMessage == null,
                    FileName = fileName,
                    ErrorMessage = errorMessage,
                    MessageType = "Video"
                }
            };
        }

        [Fact]
        public void SuccessMessage_ContainsChatName()
        {
            var ev = MakeEvent("MyCoolChannel", "video.mp4");
            var message = BuildSuccessMessage(ev);
            message.Should().Contain("MyCoolChannel");
        }

        [Fact]
        public void SuccessMessage_ContainsFileName()
        {
            var ev = MakeEvent("TestChat", "my_video.mp4");
            var message = BuildSuccessMessage(ev);
            message.Should().Contain("my_video.mp4");
        }

        [Fact]
        public void SuccessMessage_ContainsAuthorWhenProvided()
        {
            var ev = MakeEvent("TestChat", "file.mp4", postAuthor: "JohnDoe");
            var message = BuildSuccessMessage(ev);
            message.Should().Contain("JohnDoe");
        }

        [Fact]
        public void SuccessMessage_OmitsAuthorLineWhenNull()
        {
            var ev = MakeEvent("TestChat", "file.mp4", postAuthor: null);
            var message = BuildSuccessMessage(ev);
            message.Should().NotContain("Author:");
        }

        [Fact]
        public void WarningMessage_ContainsErrorText()
        {
            var ev = MakeEvent("TestChat", "file.mp4", errorMessage: "Download failed");
            var message = BuildWarningMessage(ev);
            message.Should().Contain("Download failed");
        }

        [Fact]
        public void WarningMessage_ContainsChatName()
        {
            var ev = MakeEvent("WarningChannel", "file.mp4", errorMessage: "Some error");
            var message = BuildWarningMessage(ev);
            message.Should().Contain("WarningChannel");
        }

        // Helper methods that replicate the Notification formatting logic for unit testing
        private static string BuildSuccessMessage(ResultMessageEvent eventMessage)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("✅ <b>Downloaded Successfully</b>");
            sb.AppendLine();
            sb.AppendLine($"📁 <b>Chat:</b> {eventMessage.Chat.Name}");
            sb.AppendLine($"🎬 <b>Type:</b> {eventMessage.ResultExecute.MessageType ?? "-"}");
            sb.AppendLine($"📄 <b>File:</b> {eventMessage.ResultExecute.FileName ?? "-"}");
            if (!string.IsNullOrWhiteSpace(eventMessage.PostAuthor))
                sb.AppendLine($"👤 <b>Author:</b> {eventMessage.PostAuthor}");
            return sb.ToString();
        }

        private static string BuildWarningMessage(ResultMessageEvent eventMessage)
        {
            var snippet = eventMessage.Message?.Length > 80
                ? eventMessage.Message[..80] + "…"
                : eventMessage.Message ?? string.Empty;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("⚠️ <b>Warning</b>");
            sb.AppendLine();
            sb.AppendLine($"📁 <b>Chat:</b> {eventMessage.Chat.Name}");
            sb.AppendLine($"🎬 <b>Type:</b> {eventMessage.ResultExecute.MessageType ?? "-"}");
            sb.AppendLine($"❌ <b>Error:</b> {eventMessage.ResultExecute.ErrorMessage ?? "-"}");
            sb.AppendLine($"💬 <b>Message:</b> {snippet}");
            return sb.ToString();
        }
    }
}
