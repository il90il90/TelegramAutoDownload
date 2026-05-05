using System.ComponentModel;

namespace TelegramAutoDownload.Models
{
    public class DownloadItem : INotifyPropertyChanged
    {
        private double _progress;
        private string _status = "Downloading";

        public string FileName { get; set; } = string.Empty;
        public string ChatName { get; set; } = string.Empty;
        public string PluginName { get; set; } = string.Empty;

        public double Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(nameof(Progress)); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
