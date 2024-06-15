namespace TelegramClient.Models
{
    public class ChatDto
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string Username{ get; set; }
        public bool Selected { get; set; }
        public string Type { get; set; }
        public string ReactionIcon { get; set; } = string.Empty;
    }
}
