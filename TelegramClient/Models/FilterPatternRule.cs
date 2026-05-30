namespace TelegramClient.Models
{
    public enum FilterPatternMode
    {
        Exclude,
        Include,
    }

    public class FilterPatternRule
    {
        public string Pattern { get; set; } = string.Empty;
        public FilterPatternMode Mode { get; set; } = FilterPatternMode.Exclude;
    }
}
