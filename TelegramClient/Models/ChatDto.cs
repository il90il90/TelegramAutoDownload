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

        // Pre-computed lowercase fields — set once after load so search never allocates strings per keystroke
        public string NameLower { get; set; } = string.Empty;
        public string UsernameLower { get; set; } = string.Empty;
        public string ReactionIcon { get; set; } = string.Empty;
        // Reaction sent when download STARTS (default: ⏳)
        public string DownloadStartIcon { get; set; } = string.Empty;
        public Download Download { get; set; } = new Download();
        public int DownloadFromSize { get; set; }
        public List<string> IgnoreFileByRegex { get; set; } = [];
        // Keys are PluginName values; missing key = enabled by default
        public Dictionary<string, bool> EnabledPlugins { get; set; } = new();
    }

    public class Download
    {
        public bool Videos { get; set; }
        public bool Photos { get; set; }
        public bool Music { get; set; }
        public bool Files { get; set; }

    }

}
