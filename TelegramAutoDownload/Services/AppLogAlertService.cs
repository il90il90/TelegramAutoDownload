using System;
using System.Windows;
using System.Windows.Threading;
using TelegramAutoDownload.Models;

namespace TelegramAutoDownload.Services
{
    /// <summary>
    /// Tracks warning/error log entries for UI alert (blink + open log viewer at line).
    /// </summary>
    public sealed class AppLogAlertService
    {
        private static readonly Lazy<AppLogAlertService> _instance = new(() => new());
        public static AppLogAlertService Instance => _instance.Value;

        private int _unreadCount;
        private LogPointer? _latest;

        public int UnreadCount => _unreadCount;
        public LogPointer? Latest => _latest;

        public event Action? Changed;

        private AppLogAlertService() { }

        public void Report(LogPointer pointer)
        {
            _latest = pointer;
            _unreadCount++;
            RaiseChanged();
        }

        public void Clear()
        {
            if (_unreadCount == 0 && _latest == null) return;
            _unreadCount = 0;
            _latest = null;
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            var handler = Changed;
            if (handler == null) return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                handler();
            else
                dispatcher.BeginInvoke(handler, DispatcherPriority.Normal);
        }
    }
}
