using System;
using System.Threading.Tasks;
using TelegramClient.Factory.Base;
using TelegramClient.Factory.FactoriesMessages.Enum;
using TelegramClient.Models;
using TL;
using WTelegram;
using YoutubePlugin;

namespace TelegramClient.Factory.Factories
{
    public class YouTube : BaseMessage
    {
        private readonly YoutubeDownloader _youtubeDownloader;

        public override string FileExtension { get; } = "";
        public override MessageTypes TypeMessage { get => MessageTypes.YouTube; set => throw new NotImplementedException(); }

        public YouTube(Client client, string pathFolderToSaveFiles) : base(client, pathFolderToSaveFiles)
        {
            _youtubeDownloader = new YoutubeDownloader();
        }

        public override async Task ExecuteAsync(Message message, ChatDto chatDto)
        {
            if (!message.message.StartsWith("https://youtu")) return;

            var videoUrl = message.message;
            var infoVideo = await _youtubeDownloader.GetVideoInfo(videoUrl);
            var pathFolderLocation = PathLocationFolder(chatDto, infoVideo.Author.ChannelTitle,true);
            await _youtubeDownloader.DownloadYouTubeVideoAsync(pathFolderLocation, videoUrl);
        }
    }
}
