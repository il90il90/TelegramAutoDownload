using BasePlugins;
using System;
using System.Net.Http.Headers;

namespace DownloadPlugin
{
    public class DownladerPlugin<TMessage> : BasePlugin<TMessage>
    {
        public override string PluginName => "Other";

        public override bool CanHandle(Config config)
        {
            return config.Text.StartsWith("http://") || config.Text.StartsWith("https://");
        }

        public override async Task<ResultExecute> ExecuteAsync(Config config)
        {
            try
            {
                string fileName;
                long chunkSize = 20 * 1024 * 1024;

                var path = $"{config.PathSaveFile}/{PluginName}/{config.ChatName}";

                CreateDirectoryIfNotExist(path);
                using (HttpClient client = new HttpClient())
                {
                    using (HttpResponseMessage response = await client.GetAsync(config.Text, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        long totalSize = response.Content.Headers.ContentLength ?? 0;
                        fileName = response?.Content?.Headers?.ContentDisposition?.FileNameStar?.Trim('"') ?? Path.GetFileName(new Uri(config.Text).LocalPath);

                        if (totalSize == 0)
                            return new ResultExecute(config.ChatName);

                        char[] invalidChars = Path.GetInvalidFileNameChars();
                        foreach (char c in invalidChars)
                        {
                            fileName = fileName.Replace(c, ' ');
                        }

                        string filePath = Path.Combine(path, fileName);

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
                return new ResultExecute(config.ChatName)
                {
                    IsSuccess = true,
                    FileName = fileName
                };
            }
            catch (Exception e)
            {
                return new ResultExecute(config.ChatName)
                {
                    ErrorMessage = e.Message,
                };
            }
        }
    }

}
