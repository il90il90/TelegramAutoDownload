using MahApps.Metro.Controls;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using TelegramAutoDownload.Models;
using TelegramAutoDownload.Services;
using TelegramClient;
using TelegramClient.Models;

namespace TelegramAutoDownload
{
    public partial class MainWindow : MetroWindow
    {
        private readonly TelegramApp TelegramApp;
        private readonly ConfigFile ConfigFile;
        private IList<ChatDto> _chats = new List<ChatDto>();
        private Notification _notification = null!;

        // Suppress event handlers while loading data to prevent redundant file I/O
        private bool _isLoading = false;

        public MainWindow(TelegramApp telegram, ConfigFile config)
        {
            InitializeComponent();

            TelegramApp = telegram;
            ConfigFile = config;
            Loaded += MainWindow_Loaded;

            var configParams = config.Read();
            _notification = new Notification(configParams);
            telegram.OnSaved = _notification.OnUpdateResultMessageAsync;
            telegram.OnWarnningMessage = _notification.OnWarnningMessageAsync;

            // Wire download progress reporting to the UI panel and Telegram live updates
            telegram.OnProgress = (chatName, fileName, pluginName, pct, bytes, total) =>
            {
                DownloadProgressService.Instance.UpdateProgress(chatName, fileName, pct, bytes, total, pluginName);
                _ = _notification.OnProgressAsync(chatName, fileName, pluginName, pct);
            };
            telegram.OnComplete = (chatName, fileName, success) =>
                DownloadProgressService.Instance.CompleteDownload(chatName, fileName, success);

            // Bind active downloads panel and keep badge count in sync
            dgDownloads.ItemsSource = DownloadProgressService.Instance.Downloads;
            DownloadProgressService.Instance.Downloads.CollectionChanged += (_, __) =>
                tbDownloadCount.Text = DownloadProgressService.Instance.Downloads.Count.ToString();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            mainLoadingRing.IsActive = true;
            tbLoadingStatus.Text = "Loading chats...";

            // Ensure yt-dlp is available for social media downloads (runs in background)
            _ = YtdlpService.EnsureAsync();

            await Task.Delay(500);
            await InitAsync();
            mainLoadingRing.IsActive = false;
            tbLoadingStatus.Text = string.Empty;
        }

        private async Task InitAsync()
        {
            await LoadDataAsync();
            ConfigParams configParams = ConfigFile.Read();
            if (configParams?.Chats == null) return;
            tbCountChats.Text = _chats.Count.ToString();

            TelegramApp.UpdateConfig(configParams);
            UpdatePathOnUI(configParams.PathSaveFile);

            // Send startup notification (skipped if bot is not configured)
            var monitoredCount = configParams.Chats?.Count(c => c.Selected) ?? 0;
            await TelegramApp.WaitForLoginAsync();
            _ = _notification.SendStartupNotificationAsync(monitoredCount, TelegramApp.Client.UserId != 0);
        }

        private async Task LoadDataAsync()
        {
            try
            {
                ConfigParams configParams = ConfigFile.Read();

                // Run the Telegram API call on a background thread to avoid
                // blocking the WPF SynchronizationContext (prevents "Not Responding")
                _chats = await Task.Run(() => TelegramApp.GetAllChats());

                foreach (var chat in _chats)
                {
                    var fromConfigFile = configParams.Chats?.FirstOrDefault(a => a.Id == chat.Id);
                    if (fromConfigFile == null) continue;

                    chat.Selected = fromConfigFile.Selected;
                    chat.ReactionIcon = fromConfigFile.ReactionIcon;
                    chat.DownloadStartIcon = fromConfigFile.DownloadStartIcon;
                    if (fromConfigFile.Download != null)
                    {
                        chat.Download.Videos = fromConfigFile.Download.Videos;
                        chat.Download.Photos = fromConfigFile.Download.Photos;
                        chat.Download.Music = fromConfigFile.Download.Music;
                        chat.Download.Files = fromConfigFile.Download.Files;
                    }
                    chat.DownloadFromSize = fromConfigFile.DownloadFromSize;
                    chat.IgnoreFileByRegex = fromConfigFile.IgnoreFileByRegex;
                    chat.EnabledPlugins = fromConfigFile.EnabledPlugins ?? new Dictionary<string, bool>();
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _isLoading = true;
                    ItemsListView.ItemsSource = _chats.OrderByDescending(a => a.Selected);
                    _isLoading = false;
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(ConfigFile) { Owner = this };
            if (settingsWindow.ShowDialog() == true)
            {
                // Reload and re-apply config after settings are saved
                var config = ConfigFile.Read();
                TelegramApp.UpdateConfig(config);
                UpdatePathOnUI(config.PathSaveFile);

                // Re-wire notification service with potentially new bot credentials
                _notification = new Notification(config);
                TelegramApp.OnSaved = _notification.OnUpdateResultMessageAsync;
                TelegramApp.OnWarnningMessage = _notification.OnWarnningMessageAsync;
            }
        }

        private void BtnSelectPath_Click(object sender, RoutedEventArgs e)
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var dialog = new OpenFolderDialog
            {
                Title = "Select a folder",
                InitialDirectory = userProfile
            };

            if (dialog.ShowDialog() == true)
            {
                var config = ConfigFile.Read();
                config.PathSaveFile = dialog.FolderName;
                ConfigFile.Save(config);
                TelegramApp.UpdateConfig(config);
                UpdatePathOnUI(dialog.FolderName);
            }
        }

        private void UpdatePathOnUI(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            tbFolderPath.Text = path;
            btnOpenFolder.IsEnabled = true;
            btnOpenFolder.ToolTip = path;
        }

        private void ThreadsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Threads are configured in Settings window — this handler is kept to avoid XAML errors.
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBox? textBox = sender as TextBox;
            string textSearch = textBox?.Text.ToLower() ?? string.Empty;
            if (_chats == null) return;

            var chats = _chats.Cast<ChatDto>().Where(c =>
                c.Name.ToLower().Contains(textSearch) ||
                c.Id.ToString().Contains(textSearch) ||
                (c.Username != null && c.Username.ToLower().Contains(textSearch)) ||
                c.Type.Contains(textSearch, StringComparison.CurrentCultureIgnoreCase))
                .OrderByDescending(a => a.Selected);

            ItemsListView.ItemsSource = chats;
            tbCountChats.Text = chats.Count().ToString();
        }

        private void SelectChatId_Checked(object sender, RoutedEventArgs e)
        {
            if (ItemsListView.ItemsSource == null)
                return;

            ConfigParams configParams = ConfigFile.Read();

            // Merge by ID: update existing, add new selected, keep unselected with their settings
            foreach (var chat in _chats.Cast<ChatDto>())
            {
                var existingChat = configParams.Chats.FirstOrDefault(a => a.Id == chat.Id);
                if (chat.Selected)
                {
                    if (existingChat != null)
                    {
                        existingChat.Selected = true;
                        chat.Download = existingChat.Download;
                        chat.ReactionIcon = existingChat.ReactionIcon;
                    }
                    else
                    {
                        configParams.Chats.Add(chat);
                    }
                }
                else if (existingChat != null)
                {
                    existingChat.Selected = false;
                }
            }

            ConfigFile.Save(configParams);
            TelegramApp.UpdateConfig(configParams);
        }

        private void HlOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = tbFolderPath.Text;
                if (!string.IsNullOrEmpty(path) && path != "No folder selected")
                    Process.Start("explorer.exe", path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TelegramAutoDownload.Models.DownloadItem item)
                DownloadProgressService.Instance.CancelDownload(item.ChatName, item.FileName);
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var comboBox = sender as ComboBox;
            if (comboBox != null && comboBox.SelectedItem != null)
            {
                var item = (ComboBoxItem)comboBox.SelectedValue;
                var reactionIcon = (string)item.Content;
                var dataContext = comboBox.DataContext as ChatDto;
                if (dataContext != null)
                {
                    var config = ConfigFile.Read();
                    var foundChat = config.Chats.FirstOrDefault(a => a.Id == dataContext.Id);
                    if (foundChat == null)
                    {
                        MessageBox.Show($"Please select a {dataContext?.Type} before choosing a Reaction.", "", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    dataContext.ReactionIcon = reactionIcon;
                    foundChat.ReactionIcon = reactionIcon;

                    ConfigFile.Save(config);
                    TelegramApp.UpdateConfig(config);
                }
            }
        }

        private void ReactionIcon_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox comboBox) return;
            _isLoading = true;
            DependencyObject parent = VisualTreeHelper.GetParent(comboBox);
            while (parent is not ListViewItem && parent != null)
                parent = VisualTreeHelper.GetParent(parent);

            if (parent is ListViewItem listViewItem)
            {
                var chatDto = listViewItem.DataContext as ChatDto;
                if (chatDto != null)
                    comboBox.Text = chatDto.ReactionIcon;
            }
            _isLoading = false;
        }

        private void DownloadStartIcon_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not ComboBox comboBox || comboBox.SelectedItem == null) return;

            var icon = (string)((ComboBoxItem)comboBox.SelectedValue).Content;
            var dataContext = comboBox.DataContext as ChatDto;
            if (dataContext == null) return;

            var config = ConfigFile.Read();
            var foundChat = config.Chats.FirstOrDefault(a => a.Id == dataContext.Id);
            if (foundChat == null)
            {
                MessageBox.Show($"Please select the chat first.", "", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            dataContext.DownloadStartIcon = icon;
            foundChat.DownloadStartIcon = icon;
            ConfigFile.Save(config);
            TelegramApp.UpdateConfig(config);
        }

        private void DownloadStartIcon_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox comboBox) return;
            _isLoading = true;
            DependencyObject parent = VisualTreeHelper.GetParent(comboBox);
            while (parent is not ListViewItem && parent != null)
                parent = VisualTreeHelper.GetParent(parent);

            if (parent is ListViewItem listViewItem)
            {
                var chatDto = listViewItem.DataContext as ChatDto;
                if (chatDto != null)
                    comboBox.Text = chatDto.DownloadStartIcon;
            }
            _isLoading = false;
        }

        private void Download_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var checkbox = sender as CheckBox;
            if (checkbox?.IsChecked != null)
            {
                var configParams = ConfigFile.Read();

                var chatDto = checkbox.DataContext as ChatDto;
                var chat = configParams.Chats.FirstOrDefault(a => a.Id == chatDto?.Id);
                if (chat == null) return;

                switch (checkbox.Content)
                {
                    case "Videos":
                        chat.Download.Videos = checkbox.IsChecked.Value;
                        break;
                    case "Photos":
                        chat.Download.Photos = checkbox.IsChecked.Value;
                        break;
                    case "Music":
                        chat.Download.Music = checkbox.IsChecked.Value;
                        break;
                    case "Files":
                        chat.Download.Files = checkbox.IsChecked.Value;
                        break;
                    default:
                        break;
                }

                ConfigFile.Save(configParams);
                TelegramApp.UpdateConfig(configParams);
            }
        }

        private void Download_Loaded(object sender, RoutedEventArgs e)
        {
            // Intentionally empty: IsChecked is set via TwoWay binding in XAML.
            // This handler exists only to avoid XAML compilation errors.
        }

        private void Provider_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox checkBox) return;
            var chatDto = checkBox.DataContext as ChatDto;
            var pluginName = checkBox.Tag as string ?? string.Empty;
            if (chatDto == null) return;

            // Set initial value without triggering save (suppress via _isLoading)
            _isLoading = true;
            checkBox.IsChecked = chatDto.EnabledPlugins.TryGetValue(pluginName, out var enabled)
                ? enabled : true;
            _isLoading = false;
        }

        private void Provider_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is CheckBox checkBox)
            {
                var chatDto = checkBox.DataContext as ChatDto;
                var pluginName = checkBox.Tag as string ?? string.Empty;
                if (chatDto == null || string.IsNullOrEmpty(pluginName)) return;

                chatDto.EnabledPlugins[pluginName] = checkBox.IsChecked == true;

                var config = ConfigFile.Read();
                var foundChat = config.Chats.FirstOrDefault(a => a.Id == chatDto.Id);
                if (foundChat != null)
                {
                    foundChat.EnabledPlugins[pluginName] = checkBox.IsChecked == true;
                    ConfigFile.Save(config);
                    TelegramApp.UpdateConfig(config);
                }
            }
        }

        private void DownloadSize_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textbox)
            {
                var configParams = ConfigFile.Read();

                var chatDto = textbox.DataContext as ChatDto;
                var chat = configParams.Chats.FirstOrDefault(a => a.Id == chatDto?.Id);
                if (chat == null)
                    return;

                if (int.TryParse(textbox.Text, out var size))
                {
                    chat.DownloadFromSize = size;
                }
                else
                {
                    chat.DownloadFromSize = 0;
                    textbox.Text = "0";
                }

                ConfigFile.Save(configParams);
                TelegramApp.UpdateConfig(configParams);
            }
        }
    }
}
