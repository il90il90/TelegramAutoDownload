using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using TelegramAutoDownload.Models;

namespace TelegramAutoDownload.Services
{
    public class DownloadProgressService
    {
        private static readonly Lazy<DownloadProgressService> _instance = new(() => new());
        public static DownloadProgressService Instance => _instance.Value;

        public ObservableCollection<DownloadItem> Downloads { get; } = new();

        public void AddDownload(string chatName, string fileName, string pluginName)
        {
            // Dispatch to UI thread for ObservableCollection modification
            Application.Current?.Dispatcher.InvokeAsync(() =>
                Downloads.Add(new DownloadItem { ChatName = chatName, FileName = fileName, PluginName = pluginName }));
        }

        public void UpdateProgress(string chatName, string fileName, double percent)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                var item = Downloads.FirstOrDefault(d => d.ChatName == chatName && d.FileName == fileName);
                if (item != null) item.Progress = percent;
            });
        }

        public void CompleteDownload(string chatName, string fileName, bool success)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                var item = Downloads.FirstOrDefault(d => d.ChatName == chatName && d.FileName == fileName);
                if (item != null)
                {
                    item.Status = success ? "Done" : "Error";
                    item.Progress = success ? 100 : item.Progress;
                    // Auto-remove after 3 seconds
                    Task.Delay(3000).ContinueWith(_ =>
                        Application.Current?.Dispatcher.InvokeAsync(() => Downloads.Remove(item)));
                }
            });
        }
    }
}
