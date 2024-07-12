using BasePlugins;
using YoutubeExplode;
using YoutubeExplode.Videos;

namespace YoutubePlugin
{
    public class YouTubePlugin<TMessage> : BasePlugin<TMessage>
    {
        readonly YoutubeClient youtube = new();

        public override string PluginName => "YouTube";


        public override bool CanHandle(Config config)
        {
            return config.Text.StartsWith("https://youtu") || config.Text.StartsWith("https://www.youtu");
        }

        public async Task<Video> GetVideoInfo(string videoUrl)
        {
            return await youtube.Videos.GetAsync(videoUrl);
        }

        public override async Task<ResultExecute> ExecuteAsync(Config config)
        {
            try
            {
                var video = await GetVideoInfo(config.Text);
                var streamManifest = await youtube.Videos.Streams.GetManifestAsync(config.Text);

                var streamInfo = streamManifest.Streams.OrderByDescending(a => a.Size.Bytes).FirstOrDefault(a => a.Container.Name.Contains("mp4"));
                if (streamInfo != null)
                {
                    char[] invalidChars = Path.GetInvalidFileNameChars();
                    var title = video.Title;
                    foreach (char c in invalidChars)
                    {
                        title = title.Replace(c, ' ');
                    }

                    var path = $"{config.PathSaveFile}/{PluginName}/{config.ChatName}/{video.Author.ChannelTitle}";
                    CreateDirectoryIfNotExist(path);

                    await youtube.Videos.Streams.DownloadAsync(streamInfo, $"{path}/{title}-{video.Author?.ChannelTitle}.{streamInfo.Container}");
                }

                return new ResultExecute(config.ChatName)
                {
                    IsSuccess = true,
                    FileName = video.Title ?? ""
                };
            }
            catch (Exception ex)
            {
                return new ResultExecute(config.ChatName)
                {
                    ErrorMessage = ex.Message,
                };
            }
        }

    }
}
