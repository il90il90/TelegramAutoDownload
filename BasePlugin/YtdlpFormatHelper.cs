namespace BasePlugins
{
    /// <summary>
    /// Maps per-chat quality labels to yt-dlp format selector strings.
    /// The format selectors follow yt-dlp's priority syntax: preferred streams are listed
    /// first, fallback options after the slash.
    /// </summary>
    public static class YtdlpFormatHelper
    {
        /// <summary>Quality labels shown in the UI quality drop-down.</summary>
        public static readonly string[] QualityOptions =
        [
            "best", "4K", "1080p", "720p", "480p", "audio"
        ];

        /// <summary>
        /// Returns the yt-dlp format string for the given quality label.
        /// Unknown labels fall back to "best".
        /// </summary>
        public static string GetFormatString(string? quality) => quality switch
        {
            "4K"    => "bestvideo[height<=2160][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height<=2160]+bestaudio/best[height<=2160]",
            "1080p" => "bestvideo[height<=1080][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height<=1080]+bestaudio/best[height<=1080]",
            "720p"  => "bestvideo[height<=720][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height<=720]+bestaudio/best[height<=720]",
            "480p"  => "bestvideo[height<=480][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height<=480]+bestaudio/best[height<=480]",
            "audio" => "bestaudio[ext=m4a]/bestaudio",
            _       => "bestvideo[ext=mp4]+bestaudio[ext=m4a]/bestvideo+bestaudio/best"
        };

        /// <summary>Returns true when the quality label requests audio-only output.</summary>
        public static bool IsAudioOnly(string? quality) => quality == "audio";
    }
}
