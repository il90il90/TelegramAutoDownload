using Logger.Config;
using System.Text.Json;

namespace Logger.Services
{
    public class TelegramService : IConfigTelegramLogger
    {
        private static readonly HttpClient client = new HttpClient();

        public string BotToken { get; set; }
        public string ChatId { get; set; }
        public bool IsActive { get; set; }

        public TelegramService(IConfigTelegramLogger configLogger)
        {
            BotToken = configLogger.BotToken;
            ChatId = configLogger.ChatId;
            if (BotToken != null && ChatId != null)
            {
                IsActive = true;
            }
        }

        /// <summary>Sends a message and returns the Telegram message_id (0 on failure).</summary>
        public async Task<long> SendMessageAsync(string text)
        {
            if (!IsActive) return 0;

            var url = $"https://api.telegram.org/bot{BotToken}/sendMessage";
            var data = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("chat_id", ChatId),
                new KeyValuePair<string, string>("text", text),
                new KeyValuePair<string, string>("parse_mode", "HTML")
            ]);

            try
            {
                var response = await client.PostAsync(url, data);
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                return doc.RootElement.GetProperty("result").GetProperty("message_id").GetInt64();
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Edits a previously sent message.</summary>
        public async Task EditMessageAsync(long messageId, string text)
        {
            if (!IsActive || messageId == 0) return;

            var url = $"https://api.telegram.org/bot{BotToken}/editMessageText";
            var data = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("chat_id", ChatId),
                new KeyValuePair<string, string>("message_id", messageId.ToString()),
                new KeyValuePair<string, string>("text", text),
                new KeyValuePair<string, string>("parse_mode", "HTML")
            ]);

            try { await client.PostAsync(url, data); }
            catch { /* non-critical */ }
        }
    }
}
