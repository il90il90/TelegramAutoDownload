using BasePlugins;
using DownloadPlugin;
using PluginConfig = BasePlugins.Config;
using FluentAssertions;
using RichardSzalay.MockHttp;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    /// <summary>
    /// Tests for DownladerPlugin using a MockHttpMessageHandler so no real HTTP
    /// traffic is made. Each test creates an isolated temp directory so downloads
    /// never touch the real file system.
    /// </summary>
    public class DownloadPluginTests : IDisposable
    {
        private readonly string _tmpDir = Path.Combine(Path.GetTempPath(), $"dltest_{Guid.NewGuid():N}");

        public DownloadPluginTests() => Directory.CreateDirectory(_tmpDir);
        public void Dispose()
        {
            try { Directory.Delete(_tmpDir, recursive: true); } catch { }
        }

        private PluginConfig MakeConfig(string url) => new PluginConfig
        {
            ChatName  = "TestChat",
            Text      = url,
            PathSaveFile = _tmpDir,
            EnabledPlugins = new System.Collections.Generic.Dictionary<string, bool>()
        };

        // ---------------------------------------------------------------------------
        // CanHandle
        // ---------------------------------------------------------------------------

        [Theory]
        [InlineData("https://example.com/file.zip",   true)]
        [InlineData("http://example.com/file.zip",    true)]
        [InlineData("ftp://example.com/file.zip",     false)]
        [InlineData("magnet:?xt=...",                 false)]
        [InlineData("just plain text",                false)]
        [InlineData("",                               false)]
        public void CanHandle_VariousUrls(string url, bool expected)
        {
            var plugin = new DownladerPlugin<object>();
            var config = new PluginConfig { Text = url, PathSaveFile = _tmpDir, ChatName = "c", EnabledPlugins = new() };
            plugin.CanHandle(config).Should().Be(expected);
        }

        // ---------------------------------------------------------------------------
        // Successful download
        // ---------------------------------------------------------------------------

        [Fact]
        public async Task ExecuteAsync_ValidUrl_ReturnsSuccessAndCreatesFile()
        {
            const string url      = "https://example.com/sample.bin";
            var content = new byte[1024]; // 1 KB
            new Random(42).NextBytes(content);

            var mock = new MockHttpMessageHandler();

            // Initial HEAD-like GET (ResponseHeadersRead) — returns headers + body
            mock.When(url)
                .Respond(req =>
                {
                    var resp = new HttpResponseMessage(HttpStatusCode.OK);
                    resp.Content = new ByteArrayContent(content);
                    resp.Content.Headers.ContentLength = content.Length;
                    resp.Content.Headers.ContentDisposition =
                        new ContentDispositionHeaderValue("attachment") { FileNameStar = "sample.bin" };
                    return resp;
                });

            var plugin = new DownladerPlugin<object>(new HttpClient(mock));

            string? progressFile  = null;
            string? completeFile  = null;
            plugin.OnProgress = (_, file, _, _, _, _) => progressFile = file;
            plugin.OnComplete = (_, file, _)            => completeFile = file;

            var result = await plugin.ExecuteAsync(MakeConfig(url));

            result.IsSuccess.Should().BeTrue();
            result.FileName.Should().Be("sample.bin");
            result.ErrorMessage.Should().BeNullOrEmpty();

            File.Exists(result.FilePath).Should().BeTrue("file must be saved to disk");
            completeFile.Should().Be("sample.bin", "OnComplete must fire with the filename");
        }

        // ---------------------------------------------------------------------------
        // Zero-length response
        // ---------------------------------------------------------------------------

        [Fact]
        public async Task ExecuteAsync_ZeroContentLength_ReturnsNotSuccess()
        {
            const string url = "https://example.com/empty.bin";

            var mock = new MockHttpMessageHandler();
            mock.When(url).Respond(req =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK);
                resp.Content = new ByteArrayContent(Array.Empty<byte>());
                resp.Content.Headers.ContentLength = 0;
                resp.Content.Headers.ContentDisposition =
                    new ContentDispositionHeaderValue("attachment") { FileNameStar = "empty.bin" };
                return resp;
            });

            var plugin  = new DownladerPlugin<object>(new HttpClient(mock));
            var result  = await plugin.ExecuteAsync(MakeConfig(url));

            result.IsSuccess.Should().BeFalse(
                "a 0-byte response is treated as 'nothing to download' and must not succeed");
        }

        // ---------------------------------------------------------------------------
        // HTTP error
        // ---------------------------------------------------------------------------

        [Fact]
        public async Task ExecuteAsync_ServerError_ReturnsErrorMessage()
        {
            const string url = "https://example.com/notfound.bin";

            var mock = new MockHttpMessageHandler();
            mock.When(url).Respond(HttpStatusCode.NotFound);

            var plugin = new DownladerPlugin<object>(new HttpClient(mock));
            var result = await plugin.ExecuteAsync(MakeConfig(url));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().NotBeNullOrEmpty("error details must be reported");
        }

        // ---------------------------------------------------------------------------
        // Cancellation
        // ---------------------------------------------------------------------------

        [Fact]
        public async Task ExecuteAsync_CancelledBeforeStart_ReturnsNotSuccess()
        {
            const string url = "https://example.com/cancel.bin";
            var content = new byte[10 * 1024 * 1024]; // 10 MB to ensure we'd block

            var mock = new MockHttpMessageHandler();
            mock.When(url).Respond(req =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK);
                resp.Content = new ByteArrayContent(content);
                resp.Content.Headers.ContentLength = content.Length;
                resp.Content.Headers.ContentDisposition =
                    new ContentDispositionHeaderValue("attachment") { FileNameStar = "cancel.bin" };
                return resp;
            });

            var plugin = new DownladerPlugin<object>(new HttpClient(mock));
            var config = MakeConfig(url);

            // Register cancellation key and cancel it immediately after registration
            // by hooking OnProgress to cancel as soon as download starts
            bool cancelled = false;
            plugin.OnProgress = (chatName, fileName, _, _, _, _) =>
            {
                if (!cancelled)
                {
                    cancelled = true;
                    CancellationRegistry.Cancel(CancellationRegistry.MakeKey(chatName, fileName));
                }
            };

            var result = await plugin.ExecuteAsync(config);

            result.IsSuccess.Should().BeFalse("a cancelled download must not report success");
        }

        // ---------------------------------------------------------------------------
        // Filename fallback from URL
        // ---------------------------------------------------------------------------

        [Fact]
        public async Task ExecuteAsync_NoContentDisposition_UsesUrlFilename()
        {
            const string url = "https://example.com/path/to/report.pdf";
            var content = new byte[512];

            var mock = new MockHttpMessageHandler();
            mock.When(url).Respond(req =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK);
                resp.Content = new ByteArrayContent(content);
                resp.Content.Headers.ContentLength = content.Length;
                // No Content-Disposition header — plugin should fall back to URL path
                return resp;
            });

            var plugin = new DownladerPlugin<object>(new HttpClient(mock));
            var result = await plugin.ExecuteAsync(MakeConfig(url));

            result.IsSuccess.Should().BeTrue();
            result.FileName.Should().Be("report.pdf",
                "when Content-Disposition is absent the filename must be inferred from the URL path");
        }
    }
}
