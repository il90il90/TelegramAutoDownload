using MahApps.Metro.Controls;
using System;
using System.Threading.Tasks;
using System.Windows;
using TelegramAutoDownload.Services;

namespace TelegramAutoDownload
{
    public partial class UpdateDialog : MetroWindow
    {
        private readonly ReleaseInfo _release;
        private bool _installing = false;

        public UpdateDialog(ReleaseInfo release)
        {
            InitializeComponent();
            _release = release;

            tbCurrentVersion.Text = $"Current: v{AppVersion.Current}";
            tbNewVersion.Text = $"New: v{release.Version}";
            tbChangelog.Text = release.Changelog;
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            if (_installing) return;
            _installing = true;
            btnInstall.IsEnabled = false;
            btnSkip.IsEnabled = false;
            pnlProgress.Visibility = Visibility.Visible;
            tbSkipInfo.Visibility = Visibility.Collapsed;

            try
            {
                await AutoUpdateService.DownloadAndInstallAsync(_release, pct =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        progressBar.Value = pct;
                        tbProgressLabel.Text = pct < 100 ? $"Downloading… {pct}%" : "Installing…";
                    });
                });
                // App will restart — control won't reach here
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Update failed: {ex.Message}\n\nPlease download manually from GitHub.",
                    "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                btnInstall.IsEnabled = true;
                btnSkip.IsEnabled = true;
                pnlProgress.Visibility = Visibility.Collapsed;
                _installing = false;
            }
        }

        private void BtnSkip_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
