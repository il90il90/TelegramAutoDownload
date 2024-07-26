using Logger.Config;
using Logger.Services;
using Newtonsoft.Json;
using TelegramClient.Models;

namespace TelegramAutoDownload
{
    public class Notification
    {
        private readonly TelegramService telegramService;
        public Notification()
        {
            telegramService = new TelegramService(new ConfigTelegramLogger
            {
                BotToken = Environment.GetEnvironmentVariable("BOT_TOKEN"),
                ChatId = Environment.GetEnvironmentVariable("CHAT_ID"),
            });
        }

        public async Task<ResultMessageEvent> OnUpdateResultMessageAsync(ResultMessageEvent eventMessage)
        {
            await telegramService.SendMessageAsync("✅\n" + JsonConvert.SerializeObject(new
            {
                Message = eventMessage.Message,
                Chat = eventMessage.Chat,
                PostAuthor = eventMessage.PostAuthor,
            }, Formatting.Indented));

            return eventMessage;
        }

        public async Task<ResultMessageEvent> OnWarnningMessageAsync(ResultMessageEvent eventMessage)
        {
            await telegramService.SendMessageAsync("⚠️\n" + JsonConvert.SerializeObject(new
            {
                Message = eventMessage.Message,
                Chat = eventMessage.Chat,
                ResultExecute = eventMessage.ResultExecute,
                PostAuthor = eventMessage.PostAuthor,
            }, Formatting.Indented));

            return eventMessage;
        }
    }
}
