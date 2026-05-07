using ControlzEx.Theming;
using MahApps.Metro.Controls;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Navigation;
using TelegramAutoDownload.Models;
using TelegramClient;

namespace TelegramAutoDownload
{
    public partial class SettingsWindow : MetroWindow
    {
        private readonly ConfigFile  _configFile;
        private readonly TelegramApp? _telegram;
        private ConfigParams         _config;

        public SettingsWindow(ConfigFile configFile, TelegramApp? telegram = null)
        {
            InitializeComponent();
            _configFile = configFile;
            _telegram   = telegram;
            _config     = _configFile.Read();
            LoadValues();

            // Hide the logout button if Telegram is not connected
            BtnLogout.Visibility = telegram != null ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadValues()
        {
            // Telegram API
            txtAppId.Text = _config.AppId > 0 ? _config.AppId.ToString() : string.Empty;
            pbApiHash.Password = _config.ApiHash ?? string.Empty;

            // Notification Bot
            pbBotToken.Text = _config.BotToken ?? string.Empty;
            txtChatId.Text = _config.ChatId ?? string.Empty;
            // Toggle is ON if bot token is filled in; fields are always enabled
            toggleNotifications.IsChecked = !string.IsNullOrWhiteSpace(_config.BotToken);

            // General
            txtDownloadPath.Text = _config.PathSaveFile ?? string.Empty;
            sliderThreads.Value = Math.Max(1, Math.Min(10, _config.DownloadThreads));
            tbThreadsValue.Text = ((int)sliderThreads.Value).ToString();
            toggleDarkMode.IsChecked = _config.DarkMode;

            // Notification preferences
            chkNotifyStartup.IsChecked  = _config.NotifyOnStartup;
            chkNotifyProgress.IsChecked = _config.NotifyOnProgress;
            chkNotifyComplete.IsChecked = _config.NotifyOnComplete;
            chkNotifyError.IsChecked    = _config.NotifyOnError;

        }

        private void ToggleNotifications_Changed(object sender, RoutedEventArgs e)
        {
            // Toggle only controls whether notifications are active — fields stay always editable
        }

        private void ToggleDarkMode_Changed(object sender, RoutedEventArgs e)
        {
            bool dark = toggleDarkMode.IsChecked == true;
            ThemeManager.Current.ChangeTheme(Application.Current, dark ? "Dark.Blue" : "Light.Blue");
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

            // Validate: if toggle is on, bot token is required
            bool notificationsEnabled = toggleNotifications.IsChecked == true;
            if (notificationsEnabled && string.IsNullOrWhiteSpace(pbBotToken.Text))
            {
                MessageBox.Show("Enter a Bot Token to enable notifications, or turn the toggle off.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                pbBotToken.Focus();
                return;
            }

            // Detect if API credentials changed — user will need to re-login
            bool apiChanged = appId != _config.AppId ||
                              pbApiHash.Password != (_config.ApiHash ?? string.Empty);

            _config.AppId = appId;
            _config.ApiHash = pbApiHash.Password;
            // Always save whatever the user typed — toggle controls sending, not storage
            _config.BotToken = pbBotToken.Text;
            _config.ChatId = txtChatId.Text.Trim();
            _config.PathSaveFile = txtDownloadPath.Text.Trim();
            _config.DownloadThreads = (int)sliderThreads.Value;
            _config.DarkMode = toggleDarkMode.IsChecked == true;

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

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export settings",
                Filter = "JSON files (*.json)|*.json",
                FileName = "TelegramAutoDownload-settings.json"
            };
            if (dialog.ShowDialog() != true) return;

            // Export without sensitive credentials (AppId / ApiHash excluded)
            var export = new ConfigParams
            {
                AppId = 0,
                ApiHash = string.Empty,
                BotToken = _config.BotToken,
                ChatId = _config.ChatId,
                PathSaveFile = _config.PathSaveFile,
                DownloadThreads = _config.DownloadThreads,
                DarkMode = _config.DarkMode,
                NotifyOnStartup = _config.NotifyOnStartup,
                NotifyOnProgress = _config.NotifyOnProgress,
                NotifyOnComplete = _config.NotifyOnComplete,
                NotifyOnError = _config.NotifyOnError,
                Chats = _config.Chats
            };
            File.WriteAllText(dialog.FileName, JsonConvert.SerializeObject(export, Formatting.Indented));
            MessageBox.Show("Settings exported successfully.\n(API credentials were excluded for security.)",
                "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import settings",
                Filter = "JSON files (*.json)|*.json"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var json = File.ReadAllText(dialog.FileName);
                var imported = JsonConvert.DeserializeObject<ConfigParams>(json);
                if (imported == null) throw new Exception("Invalid file format.");

                // Preserve existing API credentials — import only non-secret settings
                if (!string.IsNullOrWhiteSpace(imported.BotToken)) _config.BotToken = imported.BotToken;
                if (!string.IsNullOrWhiteSpace(imported.ChatId)) _config.ChatId = imported.ChatId;
                if (!string.IsNullOrWhiteSpace(imported.PathSaveFile)) _config.PathSaveFile = imported.PathSaveFile;
                if (imported.DownloadThreads > 0) _config.DownloadThreads = imported.DownloadThreads;
                _config.DarkMode = imported.DarkMode;
                _config.NotifyOnStartup = imported.NotifyOnStartup;
                _config.NotifyOnProgress = imported.NotifyOnProgress;
                _config.NotifyOnComplete = imported.NotifyOnComplete;
                _config.NotifyOnError = imported.NotifyOnError;
                if (imported.Chats?.Count > 0) _config.Chats = imported.Chats;

                LoadValues();
                MessageBox.Show("Settings imported successfully.", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnTestBot_Click(object sender, RoutedEventArgs e)
        {
            var token = pbBotToken.Text.Trim();
            var chatId = txtChatId.Text.Trim();

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chatId))
            {
                MessageBox.Show("Please enter Bot Token and Chat ID first.", "Test Bot",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnTestBot.IsEnabled = false;
            btnTestBot.Content = "Sending...";
            try
            {
                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromSeconds(10);
                var url = $"https://api.telegram.org/bot{token}/sendMessage" +
                          $"?chat_id={Uri.EscapeDataString(chatId)}" +
                          $"&text={Uri.EscapeDataString("✅ TelegramAutoDownload — bot is working correctly!")}";
                var resp = await http.GetAsync(url);
                if (resp.IsSuccessStatusCode)
                    MessageBox.Show("✅ Message sent successfully! Check your Telegram.", "Test Bot",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                else
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    MessageBox.Show($"❌ Failed: {body}", "Test Bot",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Error: {ex.Message}", "Test Bot",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnTestBot.IsEnabled = true;
                btnTestBot.Content = "Test Bot";
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private async void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "This will log you out of Telegram and delete the local session.\n\n" +
                "The app will restart and ask for your phone number again.\n\n" +
                "Continue?",
                "Logout",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            BtnLogout.IsEnabled = false;
            BtnLogout.Content   = "Logging out…";

            if (_telegram != null)
                await _telegram.LogoutAsync();

            // Restart the application so it goes through the login flow cleanly
            var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });

            Application.Current.Shutdown();
        }
    }
}
