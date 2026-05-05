using MahApps.Metro.Controls;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using TelegramAutoDownload.Services;

namespace TelegramAutoDownload
{
    public partial class UpdateDialog : MetroWindow
    {
        private readonly ReleaseInfo _release;
        private bool _installing = false;

        /// <summary>True if the user clicked Skip (not Install).</summary>
        public bool WasSkipped { get; private set; } = false;

        public UpdateDialog(ReleaseInfo release)
        {
            InitializeComponent();
            _release = release;

            tbCurrentVersion.Text = $"Current: v{AppVersion.Current}";
            tbNewVersion.Text = $"New: v{release.Version}";
            tbChangelog.Text = FormatChangelog(release.Changelog);
        }

        /// <summary>Strips Markdown syntax so the changelog reads as plain text.</summary>
        private static string FormatChangelog(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return "No changelog available.";

            var text = markdown;
            // Remove heading hashes (##, ###, etc.)
            text = Regex.Replace(text, @"^#{1,6}\s*", string.Empty, RegexOptions.Multiline);
            // Bold: **text** or __text__
            text = Regex.Replace(text, @"\*\*(.+?)\*\*|__(.+?)__", m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
            // Italic: *text* or _text_
            text = Regex.Replace(text, @"\*(.+?)\*|_(.+?)_", m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
            // Collapse 3+ blank lines to 2
            text = Regex.Replace(text, @"\n{3,}", "\n\n");
            return text.Trim();
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
            WasSkipped = true;
            Close();
        }
    }
}
