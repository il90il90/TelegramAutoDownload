using System.Collections.Generic;
using System.Threading;

namespace BasePlugins
{
    public class Config
    {
        public required string ChatName { get; set; }
        public required string Text { get; set; }
        public required string PathSaveFile { get; set; }
        // Keys are PluginName values; missing key means enabled by default
        public Dictionary<string, bool> EnabledPlugins { get; set; } = new();
        // Cancellation token registered by the host so the UI cancel button can abort the download
        public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
        // yt-dlp quality label: "best", "4K", "1080p", "720p", "480p", "audio"
        public string YtdlpQuality { get; set; } = "best";
    }
}
