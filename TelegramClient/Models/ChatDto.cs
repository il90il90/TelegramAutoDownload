using System.Collections.Generic;

namespace TelegramClient.Models
{
    public class ChatDto
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string Username { get; set; }
        public bool Selected { get; set; }
        public string Type { get; set; }
        public string ReactionIcon { get; set; } = string.Empty;
        public Download Download { get; set; } = new Download();
        public int Size { get; set; }
        public List<string> Regex { get; set; } = [];
    }

    public class Download
    {
        public bool Videos { get; set; }
        public bool Photos { get; set; }
        public bool Music { get; set; }
        public bool Files { get; set; }

    }

}
