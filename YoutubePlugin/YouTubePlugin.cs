using BasePlugins;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace YoutubePlugin
{
    public class YouTubePlugin<TMessage> : BasePlugin<TMessage>
    {
        readonly YoutubeClient youtube = new();

        public override string PluginName => "YouTube";
        public override int Priority => 10;

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

                // Prefer muxed (video+audio) MP4 stream first for simplicity
                IStreamInfo? streamInfo = streamManifest
                    .GetMuxedStreams()
                    .OrderByDescending(s => s.VideoQuality.MaxHeight)
                    .FirstOrDefault(s => s.Container == Container.Mp4);

                // Fall back to any muxed stream
                streamInfo ??= streamManifest
                    .GetMuxedStreams()
                    .OrderByDescending(s => s.VideoQuality.MaxHeight)
                    .FirstOrDefault();

                // Fall back to highest-quality video-only MP4
                streamInfo ??= streamManifest
                    .GetVideoOnlyStreams()
                    .OrderByDescending(s => s.VideoQuality.MaxHeight)
                    .FirstOrDefault(s => s.Container == Container.Mp4);

                if (streamInfo == null)
                {
                    return new ResultExecute(config.ChatName)
                    {
                        IsSuccess = false,
                        ErrorMessage = "No suitable video stream found for this YouTube video."
                    };
                }

                // Sanitize title for use as filename
                char[] invalidChars = Path.GetInvalidFileNameChars();
                var title = video.Title;
                foreach (char c in invalidChars)
                    title = title.Replace(c, ' ');

                var path = Path.Combine(config.PathSaveFile, PluginName, config.ChatName,
                    SanitizeName(video.Author.ChannelTitle));
                CreateDirectoryIfNotExist(path);

                var ext = streamInfo.Container.Name;
                var filePath = Path.Combine(path, $"{title}.{ext}");

                await youtube.Videos.Streams.DownloadAsync(streamInfo, filePath);

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
                    ErrorMessage = $"YouTube error: {ex.Message}"
                };
            }
        }

        private static string SanitizeName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in invalid)
                name = name.Replace(c, ' ');
            return name.Trim();
        }
    }
}
