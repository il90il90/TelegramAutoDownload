using BasePlugins;
using System;
using System.Net.Http.Headers;

namespace DownloadPlugin
{
    public class DownladerPlugin<TMessage> : BasePlugin<TMessage>
    {
        private readonly HttpClient _http;

        public DownladerPlugin() : this(null) { }

        /// <param name="httpClient">
        /// Optional pre-configured client. Pass a mock handler in tests.
        /// The plugin does NOT dispose the provided client.
        /// </param>
        public DownladerPlugin(HttpClient? httpClient)
        {
            _http = httpClient ?? new HttpClient();
        }

        public override string PluginName => "Other";
        public override int Priority => 100;

        public override bool CanHandle(Config config)
        {
            return config.Text.StartsWith("http://") || config.Text.StartsWith("https://");
        }

        public override async Task<ResultExecute> ExecuteAsync(Config config)
        {
            string fileName = string.Empty;
            string filePath = string.Empty;
            string cancelKey = string.Empty;

            try
            {
                long chunkSize = 20 * 1024 * 1024;
                var path = $"{config.PathSaveFile}/{PluginName}/{config.ChatName}";
                CreateDirectoryIfNotExist(path);

                var client = _http;
                // Use a HEAD-like read to discover filename and size before registering the cancel key
                using HttpResponseMessage response = await client.GetAsync(config.Text, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                long totalSize = response.Content.Headers.ContentLength ?? 0;
                fileName = response?.Content?.Headers?.ContentDisposition?.FileNameStar?.Trim('"')
                           ?? Path.GetFileName(new Uri(config.Text).LocalPath);

                if (totalSize == 0)
                    return new ResultExecute(config.ChatName);

                char[] invalidChars = Path.GetInvalidFileNameChars();
                foreach (char c in invalidChars)
                    fileName = fileName.Replace(c, ' ');

                // Register cancellation key using the real filename so UI cancel button can find it
                cancelKey = CancellationRegistry.MakeKey(config.ChatName, fileName);
                var token = CancellationRegistry.Register(cancelKey);

                filePath = Path.Combine(path, fileName);

                // Report start
                OnProgress?.Invoke(config.ChatName, fileName, PluginName, 0, 0, totalSize);

                using FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                long offset = 0;

                while (offset < totalSize)
                {
                    token.ThrowIfCancellationRequested();

                    long end = Math.Min(offset + chunkSize - 1, totalSize - 1);
                    long chunkOffset = offset;

                    await WithRetryAsync(async () =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, config.Text);
                        request.Headers.Range = new RangeHeaderValue(chunkOffset, end);
                        using HttpResponseMessage chunkResponse = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                        chunkResponse.EnsureSuccessStatusCode();
                        using Stream contentStream = await chunkResponse.Content.ReadAsStreamAsync(token);
                        await contentStream.CopyToAsync(fileStream, token);
                        return true;
                    });

                    offset += chunkSize;

                    double pct = totalSize > 0 ? Math.Min(99, offset * 100.0 / totalSize) : 0;
                    OnProgress?.Invoke(config.ChatName, fileName, PluginName, pct, Math.Min(offset, totalSize), totalSize);
                }

                OnComplete?.Invoke(config.ChatName, fileName, true);
                CancellationRegistry.Remove(cancelKey);

                return new ResultExecute(config.ChatName) { IsSuccess = true, FileName = fileName, FilePath = filePath };
            }
            catch (OperationCanceledException)
            {
                try { if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath)) File.Delete(filePath); } catch { }
                if (!string.IsNullOrEmpty(cancelKey)) CancellationRegistry.Remove(cancelKey);
                return new ResultExecute(config.ChatName) { IsSuccess = false, FileName = fileName, ErrorMessage = "Cancelled by user" };
            }
            catch (Exception e)
            {
                OnComplete?.Invoke(config.ChatName, fileName, false);
                if (!string.IsNullOrEmpty(cancelKey)) CancellationRegistry.Remove(cancelKey);
                return new ResultExecute(config.ChatName) { ErrorMessage = e.Message, FileName = fileName };
            }
        }
    }
}
