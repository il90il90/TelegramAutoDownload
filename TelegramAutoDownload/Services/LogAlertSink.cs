using System;
using System.IO;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;
using TelegramAutoDownload.Models;

namespace TelegramAutoDownload.Services
{
    /// <summary>
    /// Raises UI alerts for warning/error log events and records a search anchor for the log viewer.
    /// </summary>
    public sealed class LogAlertSink : ILogEventSink
    {
        private readonly MessageTemplateTextFormatter _formatter;

        public LogAlertSink(string outputTemplate)
        {
            _formatter = new MessageTemplateTextFormatter(outputTemplate);
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent.Level < LogEventLevel.Warning) return;

            using var writer = new StringWriter();
            _formatter.Format(logEvent, writer);
            var rendered = writer.ToString().TrimEnd();
            var search = BuildSearchAnchor(rendered, logEvent.RenderMessage());

            var pointer = new LogPointer
            {
                FilePath = GetCurrentLogFilePath(logEvent.Timestamp),
                SearchText = search,
                Timestamp = logEvent.Timestamp,
                Level = logEvent.Level switch
                {
                    LogEventLevel.Error => "ERR",
                    LogEventLevel.Fatal => "FTL",
                    _ => "WRN",
                },
                Summary = TruncateOneLine(logEvent.RenderMessage(), 120),
            };

            AppLogAlertService.Instance.Report(pointer);
        }

        public static string GetCurrentLogFilePath(DateTimeOffset at) =>
            Path.Combine(AppPaths.LogsDir, $"app-{at:yyyyMMdd}.log");

        public static string BuildSearchAnchor(string renderedLine, string message)
        {
            // Prefer a distinctive fragment from the message body (after Serilog template prefix).
            var body = message.Trim();
            if (body.Length >= 12)
                return body.Length <= 100 ? body : body[..100];

            if (renderedLine.Length <= 100)
                return renderedLine;

            // Fall back to tail of rendered line (usually contains the message).
            return renderedLine[^Math.Min(100, renderedLine.Length)..];
        }

        private static string TruncateOneLine(string s, int max)
        {
            s = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return s.Length <= max ? s : s[..max] + "…";
        }
    }
}
