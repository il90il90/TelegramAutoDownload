using System;
using System.Collections.Generic;
using TelegramClient.Models;

namespace TelegramAutoDownload.Models
{
    public class ConfigParams
    {
        // Read credentials from environment variables; never hardcode secrets in source
        public int AppId { get; set; } = int.TryParse(Environment.GetEnvironmentVariable("APP_ID"), out var id) ? id : 0;
        public string ApiHash { get; set; } = Environment.GetEnvironmentVariable("API_HASH") ?? string.Empty;
        public int DownloadThreads { get; set; } = 3;
        public List<ChatDto> Chats { get; set; } = [];
        public string PathSaveFile { get; set; }
    }
}
