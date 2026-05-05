using MahApps.Metro.Controls;
using Microsoft.Win32;
using System;
using System.Windows;
using TelegramAutoDownload.Models;

namespace TelegramAutoDownload
{
    public partial class SettingsWindow : MetroWindow
    {
        private readonly ConfigFile _configFile;
        private ConfigParams _config;

        public SettingsWindow(ConfigFile configFile)
        {
            InitializeComponent();
            _configFile = configFile;
            _config = _configFile.Read();
            LoadValues();
        }

        private void LoadValues()
        {
            // Telegram API
            txtAppId.Text = _config.AppId > 0 ? _config.AppId.ToString() : string.Empty;
            pbApiHash.Password = _config.ApiHash ?? string.Empty;

            // Notification Bot
            bool hasBot = !string.IsNullOrWhiteSpace(_config.BotToken);
            toggleNotifications.IsChecked = hasBot;
            pbBotToken.Password = _config.BotToken ?? string.Empty;
            txtChatId.Text = _config.ChatId ?? string.Empty;
            pnlNotifications.Opacity = hasBot ? 1.0 : 0.5;
            pnlNotifications.IsEnabled = hasBot;

            // General
            txtDownloadPath.Text = _config.PathSaveFile ?? string.Empty;
            sliderThreads.Value = Math.Max(1, Math.Min(10, _config.DownloadThreads));
            tbThreadsValue.Text = ((int)sliderThreads.Value).ToString();

            // Notification preferences
            chkNotifyStartup.IsChecked  = _config.NotifyOnStartup;
            chkNotifyProgress.IsChecked = _config.NotifyOnProgress;
            chkNotifyComplete.IsChecked = _config.NotifyOnComplete;
            chkNotifyError.IsChecked    = _config.NotifyOnError;
        }

        private void ToggleNotifications_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = toggleNotifications.IsChecked == true;
            pnlNotifications.Opacity = enabled ? 1.0 : 0.5;
            pnlNotifications.IsEnabled = enabled;
        }

        private void SliderThreads_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (tbThreadsValue != null)
                tbThreadsValue.Text = ((int)e.NewValue).ToString();
        }

        private void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select download folder",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };
            if (dialog.ShowDialog() == true)
                txtDownloadPath.Text = dialog.FolderName;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // Validate App ID
            if (!int.TryParse(txtAppId.Text.Trim(), out int appId) || appId <= 0)
            {
                MessageBox.Show("Please enter a valid App ID (numeric).", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtAppId.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(pbApiHash.Password))
            {
                MessageBox.Show("API Hash cannot be empty.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                pbApiHash.Focus();
                return;
            }

            bool notificationsEnabled = toggleNotifications.IsChecked == true;
            if (notificationsEnabled && string.IsNullOrWhiteSpace(pbBotToken.Password))
            {
                MessageBox.Show("Bot Token cannot be empty when notifications are enabled.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                pbBotToken.Focus();
                return;
            }

            // Detect if API credentials changed — user will need to re-login
            bool apiChanged = appId != _config.AppId ||
                              pbApiHash.Password != (_config.ApiHash ?? string.Empty);

            _config.AppId = appId;
            _config.ApiHash = pbApiHash.Password;
            _config.BotToken = notificationsEnabled ? pbBotToken.Password : string.Empty;
            _config.ChatId = notificationsEnabled ? txtChatId.Text.Trim() : string.Empty;
            _config.PathSaveFile = txtDownloadPath.Text.Trim();
            _config.DownloadThreads = (int)sliderThreads.Value;

            // Notification preferences
            _config.NotifyOnStartup  = chkNotifyStartup.IsChecked  == true;
            _config.NotifyOnProgress = chkNotifyProgress.IsChecked == true;
            _config.NotifyOnComplete = chkNotifyComplete.IsChecked == true;
            _config.NotifyOnError    = chkNotifyError.IsChecked    == true;

            _configFile.Save(_config);

            if (apiChanged)
            {
                MessageBox.Show(
                    "API credentials were changed.\nPlease restart the application and log in again for the changes to take effect.",
                    "Restart Required", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
