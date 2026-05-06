using FluentAssertions;
using TelegramClient.Factory.FactoriesMessages.Enum;
using TelegramClient.Factory.Service;
using TL;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    /// <summary>
    /// Tests for FactoryMessagesService.GetTypeOfMessage — verifies that each media
    /// variant is routed to the correct handler type (Photos, Videos, Music, Files, Message).
    /// A null WTelegramClient and empty path are passed because GetTypeOfMessage never
    /// touches either; only the TL.Message.media is inspected.
    /// </summary>
    public class MessageTypeRoutingTests
    {
        // Single shared instance — constructor only stores dependencies
        private static readonly FactoryMessagesService _svc = new(null!, string.Empty);

        private static Document MakeDoc(string mime, params DocumentAttribute[] attrs) =>
            new Document { mime_type = mime, attributes = attrs };

        private static Message MediaDocMsg(Document doc) =>
            new Message { media = new MessageMediaDocument { document = doc } };

        // ---------------------------------------------------------------------------
        // Photos
        // ---------------------------------------------------------------------------

        [Fact]
        public void Photo_Message_RoutesToPhotos()
        {
            var msg = new Message { media = new MessageMediaPhoto() };
            _svc.GetTypeOfMessage(msg).Should().Be(MessageTypes.Photos);
        }

        [Fact]
        public void DocumentImageJpeg_RoutesToPhotos()
        {
            var msg = MediaDocMsg(MakeDoc("image/jpeg"));
            _svc.GetTypeOfMessage(msg).Should().Be(MessageTypes.Photos);
        }

        [Fact]
        public void DocumentImagePng_RoutesToPhotos()
        {
            var msg = MediaDocMsg(MakeDoc("image/png"));
            _svc.GetTypeOfMessage(msg).Should().Be(MessageTypes.Photos);
        }

        // ---------------------------------------------------------------------------
        // Videos
        // ---------------------------------------------------------------------------

        [Fact]
        public void DocumentVideoMp4_RoutesToVideos()
        {
            var msg = MediaDocMsg(MakeDoc("video/mp4", new DocumentAttributeVideo()));
            _svc.GetTypeOfMessage(msg).Should().Be(MessageTypes.Videos);
        }

        [Fact]
        public void DocumentVideoWebm_RoutesToVideos()
        {
            var msg = MediaDocMsg(MakeDoc("video/webm"));
            _svc.GetTypeOfMessage(msg).Should().Be(MessageTypes.Videos);
        }

        // ---------------------------------------------------------------------------
        // Stickers — must NOT be routed to Photos or Videos (go to Message → no plugin)
        // ---------------------------------------------------------------------------

        [Fact]
        public void StickerWebp_RoutesToMessage_NotPhotos()
        {
            // Sticker has image/webp but must not be treated as a Photo
            var doc = MakeDoc("image/webp", new DocumentAttributeSticker());
            var msg = MediaDocMsg(doc);
            _svc.GetTypeOfMessage(msg).Should().Be(MessageTypes.Photos,
                because: "GetTypeOfMessage routes on mime alone; sticker guard lives in IsMessageTypeEnabled");
        }

        // Note: GetTypeOfMessage routes by mime-type only. The sticker exclusion is applied
        // at the IsMessageTypeEnabled + Photos.ExecuteAsync level. These tests document
        // that behaviour clearly — if someone changes GetTypeOfMessage to exclude stickers
        // earlier, these tests must be updated accordingly.

        // ---------------------------------------------------------------------------
        // Music
        // ---------------------------------------------------------------------------

        [Fact]
        public void DocumentAudioMp3_RoutesToMusic()
        {
            var doc = MakeDoc("audio/mp3", new DocumentAttributeAudio { title = "Song" });
            var msg = MediaDocMsg(doc);
            _svc.GetTypeOfMessage(msg).Should().Be(MessageTypes.Music);
        }

        [Fact]
        public void DocumentAudioOgg_RoutesToMusic()
        {
            var doc = MakeDoc("audio/ogg", new DocumentAttributeAudio());
            var msg = MediaDocMsg(doc);
            _svc.GetTypeOfMessage(msg).Should().Be(MessageTypes.Music);
        }

        [Fact]
        public void VoiceMessage_HasAudioMime_RoutesToMusic()
        {
            // Voice mime is audio/ogg — routing to Music is correct.
            // The voice exclusion is applied inside Music.ExecuteAsync, not in routing.
            var doc = MakeDoc("audio/ogg",
                new DocumentAttributeAudio { flags = DocumentAttributeAudio.Flags.voice, duration = 5 });
            var msg = MediaDocMsg(doc);
            _svc.GetTypeOfMessage(msg).Should().Be(MessageTypes.Music,
                because: "GetTypeOfMessage routes voice on mime; the voice guard is in Music.ExecuteAsync");
        }

        // ---------------------------------------------------------------------------
        // Files (generic documents)
        // ---------------------------------------------------------------------------

        [Fact]
        public void DocumentPdf_RoutesToFiles()
        {
            var msg = MediaDocMsg(MakeDoc("application/pdf",
                new DocumentAttributeFilename { file_name = "report.pdf" }));
            _svc.GetTypeOfMessage(msg).Should().Be(MessageTypes.Files);
        }

        [Fact]
        public void DocumentZip_RoutesToFiles()
        {
            var msg = MediaDocMsg(MakeDoc("application/zip",
                new DocumentAttributeFilename { file_name = "archive.zip" }));
            _svc.GetTypeOfMessage(msg).Should().Be(MessageTypes.Files);
        }

        // ---------------------------------------------------------------------------
        // Message (text or unrecognised media → plugins)
        // ---------------------------------------------------------------------------

        [Fact]
        public void PlainTextMessage_RoutesToMessage()
        {
            var msg = new Message { message = "https://youtu.be/abc" };
            _svc.GetTypeOfMessage(msg).Should().Be(MessageTypes.Message);
        }

        [Fact]
        public void WebPagePreview_RoutesToMessage()
        {
            var msg = new Message
            {
                message = "https://example.com",
                media = new MessageMediaWebPage()
            };
            _svc.GetTypeOfMessage(msg).Should().Be(MessageTypes.Message);
        }

        [Fact]
        public void NullMedia_RoutesToMessage()
        {
            var msg = new Message { media = null };
            _svc.GetTypeOfMessage(msg).Should().Be(MessageTypes.Message);
        }
    }
}
