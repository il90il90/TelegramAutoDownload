using System;
using System.Collections.Generic;
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

        public int DownloadThreads { get; set; } = 3;
        public bool DarkMode { get; set; } = false;

        // Notification preferences — which events should trigger a Telegram bot message
        public bool NotifyOnStartup { get; set; } = true;
        public bool NotifyOnProgress { get; set; } = true;
        public bool NotifyOnComplete { get; set; } = true;
        public bool NotifyOnError { get; set; } = true;
        public List<ChatDto> Chats { get; set; } = [];
        public string PathSaveFile { get; set; }
    }
}
