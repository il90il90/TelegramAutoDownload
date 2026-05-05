using Logger.Config;
using Logger.Services;
using System.Text;
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
            var chat = eventMessage.Chat;
            var result = eventMessage.ResultExecute;
            var sb = new StringBuilder();
            sb.AppendLine("✅ <b>Downloaded Successfully</b>");
            sb.AppendLine();
            sb.AppendLine($"📁 <b>Chat:</b> {HtmlEncode(chat.Name)}");
            sb.AppendLine($"🎬 <b>Type:</b> {HtmlEncode(result.MessageType ?? "-")}");
            sb.AppendLine($"📄 <b>File:</b> {HtmlEncode(result.FileName ?? "-")}");
            if (!string.IsNullOrWhiteSpace(eventMessage.PostAuthor))
                sb.AppendLine($"👤 <b>Author:</b> {HtmlEncode(eventMessage.PostAuthor)}");

            await telegramService.SendMessageAsync(sb.ToString());
            return eventMessage;
        }

        public async Task<ResultMessageEvent> OnWarnningMessageAsync(ResultMessageEvent eventMessage)
        {
            var chat = eventMessage.Chat;
            var result = eventMessage.ResultExecute;
            var snippet = eventMessage.Message?.Length > 80
                ? eventMessage.Message[..80] + "…"
                : eventMessage.Message ?? string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("⚠️ <b>Warning</b>");
            sb.AppendLine();
            sb.AppendLine($"📁 <b>Chat:</b> {HtmlEncode(chat.Name)}");
            sb.AppendLine($"🎬 <b>Type:</b> {HtmlEncode(result.MessageType ?? "-")}");
            sb.AppendLine($"❌ <b>Error:</b> {HtmlEncode(result.ErrorMessage ?? "-")}");
            sb.AppendLine($"💬 <b>Message:</b> {HtmlEncode(snippet)}");

            await telegramService.SendMessageAsync(sb.ToString());
            return eventMessage;
        }

        private static string HtmlEncode(string text) =>
            text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
