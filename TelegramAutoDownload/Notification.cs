using Logger.Config;
using Logger.Services;
using System.Collections.Concurrent;
using System.Text;
using TelegramAutoDownload.Models;
using TelegramClient.Models;

namespace TelegramAutoDownload
{
    public class Notification
    {
        private readonly TelegramService telegramService;
        private readonly ConfigParams _config;

        // Tracks in-progress download message IDs so we can edit them live
        // Key = "chatName|fileName", Value = Telegram message_id
        private readonly ConcurrentDictionary<string, long> _progressMessages = new();
        // Throttle: only update Telegram every 10%
        private readonly ConcurrentDictionary<string, double> _lastReportedPct = new();

        public Notification(ConfigParams config)
        {
            _config = config;
            telegramService = new TelegramService(new ConfigTelegramLogger
            {
                BotToken = config.BotToken,
                ChatId = config.ChatId,
            });
        }

        /// <summary>
        /// Called on download progress. Sends an initial "downloading" message the first time,
        /// then edits it every ~25% to avoid Telegram notification spam.
        /// </summary>
        public async Task OnProgressAsync(string chatName, string fileName, string pluginName, double percent)
        {
            if (!telegramService.IsActive) return;
            if (!_config.NotifyOnProgress) return;

            var key = $"{chatName}|{fileName}";

            // First call: claim the slot atomically with a sentinel (0) before awaiting.
            // TryAdd is atomic — if another concurrent call already claimed it, skip.
            if (!_progressMessages.ContainsKey(key))
            {
                if (!_progressMessages.TryAdd(key, 0))
                    return; // Another concurrent call already claimed this slot

                var text = BuildProgressText(chatName, fileName, pluginName, 0);
                var msgId = await telegramService.SendMessageAsync(text);
                _progressMessages[key] = msgId; // Replace sentinel with real message ID
                _lastReportedPct[key] = 0;
                return;
            }

            // Skip if message ID is still the sentinel (initial send not finished yet)
            if (_progressMessages.TryGetValue(key, out var currentMsgId) && currentMsgId == 0)
                return;

            // Throttle: only edit every 25% to avoid spam push-notifications
            _lastReportedPct.TryGetValue(key, out var last);
            if (percent - last < 25) return;
            _lastReportedPct[key] = percent;

            var updatedText = BuildProgressText(chatName, fileName, pluginName, percent);
            _ = telegramService.EditMessageAsync(_progressMessages[key], updatedText);
        }

        public async Task<ResultMessageEvent> OnUpdateResultMessageAsync(ResultMessageEvent eventMessage)
        {
            if (!telegramService.IsActive) return eventMessage;
            if (!_config.NotifyOnComplete) return eventMessage;
            var chat = eventMessage.Chat;
            var result = eventMessage.ResultExecute;

            var lines = new List<string>
            {
                "✅ <b>Downloaded Successfully</b>",
                "",
                $"📁 <b>Chat:</b> {HtmlEncode(chat.Name)}",
                $"🎬 <b>Type:</b> {HtmlEncode(result.MessageType ?? "-")}",
                $"📄 <b>File:</b> {HtmlEncode(result.FileName ?? "-")}"
            };

            if (!string.IsNullOrWhiteSpace(result.FilePath))
                lines.Add($"📂 <b>Path:</b> <code>{HtmlEncode(result.FilePath)}</code>");

            if (!string.IsNullOrWhiteSpace(eventMessage.PostAuthor))
                lines.Add($"👤 <b>Author:</b> {HtmlEncode(eventMessage.PostAuthor)}");

            var finalText = string.Join("\n", lines);

            // Use NotificationKey if set (plugins that report progress use a temp name as key)
            var lookupKey = !string.IsNullOrEmpty(result.NotificationKey)
                ? $"{chat.Name}|{result.NotificationKey}"
                : $"{chat.Name}|{result.FileName}";

            // Wait up to 5 seconds for the initial SendMessageAsync to finish (sentinel = 0 → real ID)
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline
                   && _progressMessages.TryGetValue(lookupKey, out var pendingId)
                   && pendingId == 0)
            {
                await Task.Delay(80);
            }

            if (_progressMessages.TryRemove(lookupKey, out var msgId))
            {
                _lastReportedPct.TryRemove(lookupKey, out _);
                if (msgId != 0)
                    await telegramService.EditMessageAsync(msgId, finalText);
                else
                    await telegramService.SendMessageAsync(finalText);
            }
            else
            {
                await telegramService.SendMessageAsync(finalText);
            }

            return eventMessage;
        }

        /// <summary>
        /// Sends a startup status message. Skipped silently if bot credentials are not configured.
        /// </summary>
        public async Task SendStartupNotificationAsync(int monitoredChats, bool connected)
        {
            if (!telegramService.IsActive) return;
            if (!_config.NotifyOnStartup) return;

            string message;
            if (connected)
            {
                message = string.Join("\n",
                    "🟢 <b>TelegramAutoDownload — Started</b>",
                    "",
                    $"📡 <b>Monitoring:</b> {monitoredChats} chat(s)",
                    "✅ <b>Status:</b> Connected and listening");
            }
            else
            {
                message = string.Join("\n",
                    "🔴 <b>TelegramAutoDownload — Start Failed</b>",
                    "",
                    "❌ <b>Status:</b> Could not connect to Telegram");
            }

            try { await telegramService.SendMessageAsync(message); }
            catch { /* Do not crash the app if the notification fails */ }
        }

        public async Task<ResultMessageEvent> OnWarnningMessageAsync(ResultMessageEvent eventMessage)
        {
            if (!telegramService.IsActive) return eventMessage;
            if (!_config.NotifyOnError) return eventMessage;
            var chat = eventMessage.Chat;
            var result = eventMessage.ResultExecute;
            var snippet = eventMessage.Message?.Length > 120
                ? eventMessage.Message[..120] + "…"
                : eventMessage.Message ?? string.Empty;

            var message = string.Join("\n",
                "⚠️ <b>Warning</b>",
                "",
                $"📁 <b>Chat:</b> {HtmlEncode(chat.Name)}",
                $"🎬 <b>Type:</b> {HtmlEncode(result.MessageType ?? "-")}",
                $"❌ <b>Error:</b> {HtmlEncode(result.ErrorMessage ?? "-")}",
                $"💬 <b>Message:</b> {HtmlEncode(snippet)}");

            await telegramService.SendMessageAsync(message);
            return eventMessage;
        }

        /// <summary>
        /// Sends a rich completion summary with file size, duration, and average speed.
        /// Called from DownloadProgressService.DownloadCompleted event.
        /// </summary>
        public async Task OnDownloadCompletedAsync(string chatName, string fileName, long totalBytes, double durationSec)
        {
            if (!telegramService.IsActive) return;
            if (!_config.NotifyOnComplete) return;

            double avgSpeedBps = durationSec > 0 ? totalBytes / durationSec : 0;
            var duration = TimeSpan.FromSeconds(durationSec);
            string durationStr = duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s"
                : duration.TotalMinutes >= 1
                    ? $"{duration.Minutes}m {duration.Seconds}s"
                    : $"{duration.Seconds}s";

            string speedStr = avgSpeedBps >= 1_048_576 ? $"{avgSpeedBps / 1_048_576:F1} MB/s"
                            : avgSpeedBps >= 1024 ? $"{avgSpeedBps / 1024:F0} KB/s"
                            : $"{avgSpeedBps:F0} B/s";

            string sizeStr = totalBytes >= 1_073_741_824 ? $"{totalBytes / 1_073_741_824.0:F2} GB"
                           : totalBytes >= 1_048_576 ? $"{totalBytes / 1_048_576.0:F1} MB"
                           : totalBytes >= 1024 ? $"{totalBytes / 1024.0:F0} KB"
                           : $"{totalBytes} B";

            var key = $"{chatName}|{fileName}";
            var message = string.Join("\n",
                "✅ <b>Download Complete</b>",
                "",
                $"📁 <b>Chat:</b> {HtmlEncode(chatName)}",
                $"📄 <b>File:</b> {HtmlEncode(fileName)}",
                $"📦 <b>Size:</b> {sizeStr}",
                $"⏱ <b>Duration:</b> {durationStr}",
                $"⚡ <b>Avg Speed:</b> {speedStr}");

            if (_progressMessages.TryRemove(key, out var msgId) && msgId != 0)
            {
                _lastReportedPct.TryRemove(key, out _);
                await telegramService.EditMessageAsync(msgId, message);
            }
            else
            {
                _lastReportedPct.TryRemove(key, out _);
                await telegramService.SendMessageAsync(message);
            }
        }

        private static string BuildProgressText(string chatName, string fileName, string pluginName, double percent)
        {
            var bar = BuildProgressBar(percent);
            return string.Join("\n",
                "⬇️ <b>Downloading…</b>",
                "",
                $"📁 <b>Chat:</b> {HtmlEncode(chatName)}",
                $"📄 <b>File:</b> {HtmlEncode(fileName)}",
                $"📦 <b>Type:</b> {HtmlEncode(pluginName)}",
                $"{bar} <b>{percent:F0}%</b>");
        }

        private static string BuildProgressBar(double percent)
        {
            const int bars = 10;
            var filled = (int)(percent / 100 * bars);
            return new string('█', filled) + new string('░', bars - filled);
        }

        private static string HtmlEncode(string text) =>
            text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
