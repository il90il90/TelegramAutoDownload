using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace YoutubePlugin
{
    public class YoutubeDownloader
    {
        public async Task DownloadYouTubeVideoAsync(string videoUrl, string path)
        {
            var youtube = new YoutubeClient();
            var video = await youtube.Videos.GetAsync(videoUrl);
            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl);
            var streamInfo = streamManifest.GetMuxedStreams().GetWithHighestVideoQuality();
            await youtube.Videos.Streams.DownloadAsync(streamInfo, $"{path}/{video.Title}-{video.Author?.ChannelTitle}.{streamInfo.Container}");
        }
    }
}
