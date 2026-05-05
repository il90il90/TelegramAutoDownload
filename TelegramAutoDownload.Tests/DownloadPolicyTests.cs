using BasePlugins;
using FluentAssertions;
using TelegramClient.Factory.Base;
using TelegramClient.Models;
using TL;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    /// <summary>
    /// Thin stub to expose CheckDownloadPolicy for testing.
    /// </summary>
    internal class BaseMessageStub : BaseMessage
    {
        public BaseMessageStub() : base(null!, ".") { }
        public override TelegramClient.Factory.FactoriesMessages.Enum.MessageTypes TypeMessage =>
            TelegramClient.Factory.FactoriesMessages.Enum.MessageTypes.Files;
        public override System.Threading.Tasks.Task<ResultExecute> ExecuteAsync(Message message, ChatDto chatDto)
            => System.Threading.Tasks.Task.FromResult(new ResultExecute(chatDto.Name));
    }

    public class DownloadPolicyTests
    {
        private static ChatDto MakeChat(int minSizeMb) => new ChatDto
        {
            Name = "TestChat",
            DownloadFromSize = minSizeMb,
            IgnoreFileByRegex = new System.Collections.Generic.List<string>()
        };

        [Fact]
        public void Policy_DownloadFromSizeZero_AlwaysPasses()
        {
            // DownloadFromSize = 0 means "no threshold" — always allow
            var stub = new BaseMessageStub();
            var chat = MakeChat(0);

            // Create a message with a 1 MB document
            var message = CreateMessageWithDocument(fileSizeBytes: 1 * 1024 * 1024, filename: "test.zip");
            var result = stub.CheckDownloadPolicy(chat, message);
            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public void Policy_FileSmallerThanThreshold_Skips()
        {
            // Threshold 100 MB, file 50 MB → should skip (IsSuccess = false)
            var stub = new BaseMessageStub();
            var chat = MakeChat(100);
            var message = CreateMessageWithDocument(fileSizeBytes: 50L * 1024 * 1024, filename: "small.zip");
            var result = stub.CheckDownloadPolicy(chat, message);
            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public void Policy_FileLargerThanThreshold_Passes()
        {
            // Threshold 100 MB, file 200 MB → should download (IsSuccess = true)
            var stub = new BaseMessageStub();
            var chat = MakeChat(100);
            var message = CreateMessageWithDocument(fileSizeBytes: 200L * 1024 * 1024, filename: "large.zip");
            var result = stub.CheckDownloadPolicy(chat, message);
            result.IsSuccess.Should().BeTrue();
        }

        private static Message CreateMessageWithDocument(long fileSizeBytes, string filename)
        {
            var document = new Document
            {
                size = fileSizeBytes,
                Filename = filename,
                mime_type = "application/zip"
            };
            return new Message
            {
                media = new MessageMediaDocument
                {
                    document = document
                }
            };
        }
    }
}
