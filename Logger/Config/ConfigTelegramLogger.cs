using Logger.Interfaces;

namespace Logger.Config
{
    public interface IConfigTelegramLogger : IConfigLogger
    {
        public string BotToken  { get; set; }
        public string ChatId { get; set; }
    }

    public class ConfigTelegramLogger : IConfigTelegramLogger
    {
        public string BotToken { get; set; }
        public string ChatId { get; set; }
    }


}
