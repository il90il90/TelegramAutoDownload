using System;
using System.Collections.Generic;
using TelegramClient.Models;

namespace TelegramAutoDownload.Models
{
    public class ConfigParams
    {
        public List<ChatDto> Chats { get; set; } = new List<ChatDto>();
        public string PathSaveFile { get; set; }
        public bool ReactionStatus { get; set; }
        public string ReactionIcon { get; set; } = "👍";
    }
}
