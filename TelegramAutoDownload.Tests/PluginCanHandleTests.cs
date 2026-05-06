using BasePlugins;
using DownloadPlugin;
using FluentAssertions;
using SocialMediaPlugin;
using TorrentPlugin;
using Xunit;
using YoutubePlugin;

namespace TelegramAutoDownload.Tests
{
    public class PluginCanHandleTests
    {
        private static Config MakeConfig(string text) => new Config
        {
            ChatName = "test",
            Text = text,
            PathSaveFile = "."
        };

        // --- YouTubePlugin ---

        [Theory]
        [InlineData("https://youtu.be/dQw4w9WgXcQ")]
        [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
        [InlineData("https://youtu.be/something")]
        public void YouTube_CanHandle_ReturnsTrue_ForYouTubeUrls(string url)
        {
            var plugin = new YouTubePlugin<object>();
            plugin.CanHandle(MakeConfig(url)).Should().BeTrue();
        }

        [Theory]
        [InlineData("https://instagram.com/reel/abc")]
        [InlineData("magnet:?xt=urn:btih:abc")]
        [InlineData("some plain text")]
        public void YouTube_CanHandle_ReturnsFalse_ForNonYouTubeUrls(string url)
        {
            var plugin = new YouTubePlugin<object>();
            plugin.CanHandle(MakeConfig(url)).Should().BeFalse();
        }

        // --- SocialMediaPlugin ---

        [Theory]
        [InlineData("https://instagram.com/p/abc")]
        [InlineData("https://www.tiktok.com/@user/video/123")]
        [InlineData("http://twitter.com/user/status/123")]
        public void SocialMedia_CanHandle_ReturnsTrue_ForNonYouTubeHttps(string url)
        {
            var plugin = new SocialMediaPlugin<object>();
            plugin.CanHandle(MakeConfig(url)).Should().BeTrue();
        }

        // SocialMedia handles YouTube domains too (youtube.com + youtu.be are in _supportedDomains).
        // Priority 2 (lower number = runs first) ensures it processes YouTube links before YouTubePlugin (10).
        [Theory]
        [InlineData("https://youtu.be/dQw4w9WgXcQ")]
        [InlineData("https://www.youtube.com/watch?v=abc")]
        public void SocialMedia_CanHandle_ReturnsTrue_ForYouTubeUrls(string url)
        {
            var plugin = new SocialMediaPlugin<object>();
            plugin.CanHandle(MakeConfig(url)).Should().BeTrue();
        }

        [Theory]
        [InlineData("magnet:?xt=urn:btih:abc")]
        [InlineData("just some text")]
        public void SocialMedia_CanHandle_ReturnsFalse_ForNonHttpInput(string input)
        {
            var plugin = new SocialMediaPlugin<object>();
            plugin.CanHandle(MakeConfig(input)).Should().BeFalse();
        }

        // --- TorrentPlugin ---

        [Fact]
        public void Torrent_CanHandle_ReturnsTrue_ForMagnetLink()
        {
            var plugin = new TorrentPlugin<object>();
            plugin.CanHandle(MakeConfig("magnet:?xt=urn:btih:abc123&dn=test")).Should().BeTrue();
        }

        [Theory]
        [InlineData("https://some-site.com/file.torrent")]
        [InlineData("https://youtu.be/abc")]
        [InlineData("just text")]
        public void Torrent_CanHandle_ReturnsFalse_ForNonMagnetLinks(string text)
        {
            var plugin = new TorrentPlugin<object>();
            plugin.CanHandle(MakeConfig(text)).Should().BeFalse();
        }

        // --- DownloadPlugin ---

        [Theory]
        [InlineData("https://example.com/file.zip")]
        [InlineData("http://example.com/media.mp4")]
        public void Download_CanHandle_ReturnsTrue_ForHttpUrls(string url)
        {
            var plugin = new DownladerPlugin<object>();
            plugin.CanHandle(MakeConfig(url)).Should().BeTrue();
        }

        [Fact]
        public void Download_CanHandle_ReturnsFalse_ForMagnetLink()
        {
            var plugin = new DownladerPlugin<object>();
            plugin.CanHandle(MakeConfig("magnet:?xt=urn:btih:abc")).Should().BeFalse();
        }
    }
}
