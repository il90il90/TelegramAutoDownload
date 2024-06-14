using System;
using System.Collections.Generic;
using TelegramClient.Models;

namespace TelegramAutoDownload.Models
{
    public class ConfigParams
    {
        public int AppId { get; set; } = 2099700;
        public string ApiHash { get; set; } = "01d3e78323318d2d0a0b8766060d9152";
        public List<ChatDto> Chats { get; set; } = new List<ChatDto>();
        public string PathSaveFile { get; set; }
        public bool ReactionStatus { get; set; }
        public string ReactionIcon { get; set; } = "👍";
    }
}
