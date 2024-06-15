using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace YoutubePlugin
{
    public class YoutubeDownloader
    {
        readonly YoutubeClient youtube = new();
        public async Task<Video> GetVideoInfo(string videoUrl)
        {
            return await youtube.Videos.GetAsync(videoUrl);
        }

        public async Task DownloadYouTubeVideoAsync(string path, string videoUrl)
        {
            var video = await GetVideoInfo(videoUrl);
            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl);

            var streamInfo = streamManifest.Streams.OrderByDescending(a => a.Size.Bytes).FirstOrDefault(a => a.Container.Name.Contains("mp4"));
            if (streamInfo != null)
            {
                await youtube.Videos.Streams.DownloadAsync(streamInfo, $"{path}/{video.Title}-{video.Author?.ChannelTitle}.{streamInfo.Container}");
            }
        }
    }
}
