using Logger.Config;

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

        public async Task SendMessageAsync(string text)
        {
            if (!IsActive)
            {
                return;
            }
            var url = $"https://api.telegram.org/bot{BotToken}/sendMessage";
            var data = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("chat_id", ChatId),
                new KeyValuePair<string, string>("text", text)
            ]);

            try
            {
                var response = await client.PostAsync(url, data);
                response.EnsureSuccessStatusCode();

                string responseBody = await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine(e.Message);
            }
            await Task.CompletedTask;
        }
    }
}
