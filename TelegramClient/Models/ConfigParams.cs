using System;
using System.Collections.Generic;
using BasePlugins;
using TelegramClient;
using TelegramClient.Models;

namespace TelegramAutoDownload.Models
{
    public class ConfigParams
    {
        // Telegram client credentials — stored in config.txt; fall back to .env on first run
        public int AppId { get; set; } = int.TryParse(Environment.GetEnvironmentVariable("APP_ID"), out var id) ? id : 0;
        public string ApiHash { get; set; } = Environment.GetEnvironmentVariable("API_HASH") ?? string.Empty;

        // Notification bot credentials — stored in config.txt; fall back to .env on first run
        public string BotToken { get; set; } = Environment.GetEnvironmentVariable("BOT_TOKEN") ?? string.Empty;
        public string ChatId { get; set; } = Environment.GetEnvironmentVariable("CHAT_ID") ?? string.Empty;

        public int DownloadThreads { get; set; } = TelegramDownloadPerf.DefaultDownloadThreads;

        // Notification preferences — which events should trigger a Telegram bot message
        public bool NotifyOnStartup { get; set; } = true;
        public bool NotifyOnProgress { get; set; } = true;
        public bool NotifyOnComplete { get; set; } = true;
        public bool NotifyOnError { get; set; } = true;
        public List<ChatDto> Chats { get; set; } = [];
        public string PathSaveFile { get; set; }

        /// <summary>
        /// Per-chat yt-dlp quality UI was removed; every chat always uses best video+audio.
        /// Call after deserializing config or importing settings so legacy JSON values are overwritten.
        /// </summary>
        public void NormalizeYtDlpQualityForAllChats()
        {
            foreach (var chat in Chats)
                chat.YtdlpQuality = YtdlpFormatHelper.HighestVideoQuality;
        }
    }
}
