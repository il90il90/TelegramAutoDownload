using System.Collections.Generic;
using System.ComponentModel;

namespace TelegramClient.Models
{
    public class ChatDto : INotifyPropertyChanged
    {
        private bool _muted;
        private List<string>? _availableReactions;

        public long Id { get; set; }
        public string Name { get; set; }
        public string Username { get; set; }
        public bool Selected { get; set; }
        public string Type { get; set; }

        // Pre-computed lowercase fields — set once after load so search never allocates strings per keystroke
        public string NameLower { get; set; } = string.Empty;
        public string UsernameLower { get; set; } = string.Empty;
        public string ReactionIcon { get; set; } = string.Empty;
        // Reaction sent when download STARTS
        public string DownloadStartIcon { get; set; } = string.Empty;

        // Number of members/participants — populated from Telegram API on refresh
        public int MembersCount { get; set; }

        // Whether Telegram notifications are muted for this chat
        public bool Muted
        {
            get => _muted;
            set { _muted = value; OnPropertyChanged(nameof(Muted)); }
        }

        // Reactions available in this chat, fetched on demand from Telegram. Null means not yet loaded.
        public List<string>? AvailableReactions
        {
            get => _availableReactions;
            set
            {
                _availableReactions = value;
                OnPropertyChanged(nameof(AvailableReactions));
                OnPropertyChanged(nameof(HasReactions));
            }
        }

        // False only after reactions have been fetched and the list is confirmed empty (reactions disabled)
        public bool HasReactions => _availableReactions == null || _availableReactions.Count > 0;

        public Download Download { get; set; } = new Download();
        public int DownloadFromSize { get; set; }
        public List<string> IgnoreFileByRegex { get; set; } = [];
        // Keys are PluginName values; missing key = enabled by default
        public Dictionary<string, bool> EnabledPlugins { get; set; } = new();
        // yt-dlp quality label for URL-based plugins (YouTube, SocialMedia). Defaults to "best".
        public string YtdlpQuality { get; set; } = "best";

        /// <summary>
        /// Custom folder template for this chat. Empty = use default layout ({Type}/{ChatName}/).
        /// Supported tokens: {Type}, {ChatName}, {Year}, {Month}, {Day}
        /// Example: "{ChatName}/{Year}-{Month}" → "MyChannel/2026-05/"
        /// </summary>
        public string FolderTemplate { get; set; } = string.Empty;

        /// <summary>
        /// When true, every incoming message is appended to a JSONL history file and
        /// the full history can be exported on demand.
        /// File: {DownloadPath}/History/{ChatName}.jsonl
        /// </summary>
        public bool SaveHistory { get; set; } = false;

        /// <summary>
        /// Reaction emoji sent to Telegram when a message is appended to the history log.
        /// Empty = no reaction. Only fires when SaveHistory = true.
        /// </summary>
        public string HistoryIcon { get; set; } = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class Download
    {
        public bool Videos { get; set; }
        public bool Photos { get; set; }
        public bool Music { get; set; }
        public bool Files { get; set; }
    }
}
