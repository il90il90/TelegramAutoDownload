using BasePlugins;
using System;
using System.Net.Http.Headers;

namespace DownloadService
{
    public class DownladerPlugin<TMessage> : BasePlugin<TMessage>
    {
        public override string PluginName => "Other";

        public override bool CanHandle(Config config)
        {
            return config.Text.StartsWith("http://") || config.Text.StartsWith("https://");
        }

        public override async Task<bool> ExecuteAsync(Config config)
        {
            try
            {
                long chunkSize = 20 * 1024 * 1024;

                var path = $"{config.PathSaveFile}/{PluginName}/{config.ChatName}";

                CreateDirectoryIfNotExist(path);
                using (HttpClient client = new HttpClient())
                {
                    using (HttpResponseMessage response = await client.GetAsync(config.Text, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        long totalSize = response.Content.Headers.ContentLength ?? 0;
                        string fileName = response?.Content?.Headers?.ContentDisposition?.FileNameStar?.Trim('"') ?? Path.GetFileName(new Uri(config.Text).LocalPath);
                        string filePath = Path.Combine(path, fileName);
                        if (totalSize == 0)
                            return false;
                        using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            long offset = 0;

                            while (offset < totalSize)
                            {
                                long end = Math.Min(offset + chunkSize - 1, totalSize - 1);

                                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, config.Text);
                                request.Headers.Range = new RangeHeaderValue(offset, end);

                                using (HttpResponseMessage chunkResponse = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                                {
                                    chunkResponse.EnsureSuccessStatusCode();

                                    using (Stream contentStream = await chunkResponse.Content.ReadAsStreamAsync())
                                    {
                                        await contentStream.CopyToAsync(fileStream);
                                    }
                                }

                                offset += chunkSize;
                            }
                        }
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return false;
            }
        }
    }

}
