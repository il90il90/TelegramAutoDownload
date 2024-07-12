using Logger.Config;
using Logger.Interfaces;
using static System.Net.Mime.MediaTypeNames;

namespace Logger.Services
{
    public class TelegramService : IConfigTelegramLogger
    {
        private static readonly HttpClient client = new HttpClient();

        public string BotToken { get; set; }
        public string ChatId { get; set; }

        public TelegramService(IConfigTelegramLogger configLogger)
        {
            BotToken = configLogger.BotToken;
            ChatId = configLogger.ChatId;
        }

        public async Task SendMessageAsync(string text)
        {
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
