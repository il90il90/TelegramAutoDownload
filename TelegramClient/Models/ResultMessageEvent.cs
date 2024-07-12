using BasePlugins;

namespace TelegramClient.Models
{
    public class ResultMessageEvent
    {
        public ResultExecute ResultExecute { get; set; }
        public ChatDto Chat { get; set; }
        public string Message { get; set; }
        public string PostAuthor { get; set; }
    }
}
