using MahApps.Metro.Controls;
using Microsoft.Win32;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
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

        // Holds the pending release when an update is available (shown via blink)
        private ReleaseInfo? _pendingRelease;

        // Debounce timer for search — avoids hammering LINQ on every keystroke
        private System.Windows.Threading.DispatcherTimer? _searchDebounce;
        private string _pendingSearch = string.Empty;

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
            telegram.OnEnqueued = (chatName, msgId, previewName) =>
                DownloadProgressService.Instance.EnqueueDownload(chatName, msgId, previewName);
            telegram.OnStarted = (chatName, msgId) =>
                DownloadProgressService.Instance.StartDownload(chatName, msgId);
            telegram.OnProgress = (chatName, fileName, pluginName, pct, bytes, total) =>
            {
                DownloadProgressService.Instance.UpdateProgress(chatName, fileName, pct, bytes, total, pluginName);
                _ = _notification.OnProgressAsync(chatName, fileName, pluginName, pct);
            };
            telegram.OnComplete = (chatName, fileName, success) =>
                DownloadProgressService.Instance.CompleteDownload(chatName, fileName, success);

            // Enhanced completion notification with size, duration, avg speed
            DownloadProgressService.Instance.DownloadCompleted += (chatName, fileName, bytes, duration) =>
                _ = _notification.OnDownloadCompletedAsync(chatName, fileName, bytes, duration);

            // Bind active downloads panel and keep badge count + stats in sync
            dgDownloads.ItemsSource = DownloadProgressService.Instance.Downloads;
            DownloadProgressService.Instance.Downloads.CollectionChanged += (_, __) =>
            {
                UpdateDownloadBadges();
                UpdateStatsStrip();
            };
            DownloadProgressService.Instance.StatsChanged += UpdateStatsStrip;
            DownloadProgressService.Instance.QueueChanged += UpdateDownloadBadges;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            btnAppVersion.Content = $"v{AppVersion.Current}";
            mainLoadingRing.IsActive = true;
            tbLoadingStatus.Text = "Connecting to Telegram...";

            // Ensure yt-dlp is available and up to date silently in background
            _ = YtdlpService.EnsureAsync();

            // Wait for the Telegram session to be established BEFORE loading chats,
            // so GetAllChats doesn't block WTelegram's internal mutex
            await Task.Run(() => TelegramApp.WaitForLoginAsync(15000));

            tbLoadingStatus.Text = "Loading chats...";
            await InitAsync();
            mainLoadingRing.IsActive = false;
            tbLoadingStatus.Text = string.Empty;

            // Check for updates AFTER the window is fully loaded and rendered,
            // with a short delay so the dialog opens cleanly
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000);
                await CheckForAppUpdateAsync();
            });
        }

        private async Task InitAsync()
        {
            await LoadDataAsync();
            ConfigParams configParams = ConfigFile.Read();
            if (configParams?.Chats == null) return;
            tbCountChats.Text = _chats.Count.ToString();

            TelegramApp.UpdateConfig(configParams);
            UpdatePathOnUI(configParams.PathSaveFile);

            // Wait for login and send startup notification entirely on a background thread
            // so the UI stays responsive while the Telegram session connects
            var monitoredCount = configParams.Chats?.Count(c => c.Selected) ?? 0;
            _ = Task.Run(async () =>
            {
                await TelegramApp.WaitForLoginAsync();
                await _notification.SendStartupNotificationAsync(monitoredCount, TelegramApp.Client.UserId != 0);
            });
        }

        private async Task LoadDataAsync()
        {
            try
            {
                ConfigParams configParams = ConfigFile.Read();

                // Fetch chats AND merge config settings entirely on background thread
                _chats = await Task.Run(async () =>
                {
                    var chats = await TelegramApp.GetAllChats();
                    foreach (var chat in chats)
                    {
                        // Pre-compute lowercase strings once so search never allocates on every keystroke
                        chat.NameLower = chat.Name?.ToLowerInvariant() ?? string.Empty;
                        chat.UsernameLower = chat.Username?.ToLowerInvariant() ?? string.Empty;

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
                    return chats;
                });

                // Only the final UI binding happens on the UI thread
                _isLoading = true;
                ItemsListView.ItemsSource = _chats.OrderByDescending(a => a.Selected);
                _isLoading = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnSyncChat_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ChatDto chat) return;
            if (!chat.Selected)
            {
                MessageBox.Show("Enable monitoring for this chat first (check the checkbox), then try Sync.",
                    "Sync", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            btn.IsEnabled = false;
            btn.Content = "…";

            await TelegramApp.SyncHistoryAsync(chat, msg =>
                Dispatcher.InvokeAsync(() => tbLoadingStatus.Text = msg));

            btn.IsEnabled = true;
            btn.Content = "⬇ Sync";
            tbLoadingStatus.Text = string.Empty;
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

        private void UpdateDownloadBadges()
        {
            Dispatcher.InvokeAsync(() =>
            {
                var all = DownloadProgressService.Instance.Downloads;
                int queued = all.Count(d => d.Status == "⏳ Queued");
                int active = all.Count(d => d.Status != "⏳ Queued");

                tbDownloadCount.Text = active.ToString();
                tbQueueCount.Text = queued.ToString();
                queueCountBadge.Visibility = queued > 0
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed;
            });
        }

        private void UpdateStatsStrip()
        {
            Dispatcher.InvokeAsync(() =>
            {
                var svc = DownloadProgressService.Instance;
                tbStatsFiles.Text = $"{svc.TotalFilesDownloaded} file{(svc.TotalFilesDownloaded == 1 ? "" : "s")}";
                tbStatsBytes.Text = FormatBytes(svc.TotalBytesDownloaded);
                tbStatsActive.Text = $"{svc.Downloads.Count} active";
            });
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
            if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:F0} KB";
            return $"{bytes} B";
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _pendingSearch = (sender as TextBox)?.Text ?? string.Empty;

            // Create the timer once; attach Tick only once to avoid duplicate handlers
            if (_searchDebounce == null)
            {
                _searchDebounce = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(250)
                };
                _searchDebounce.Tick += (_, _) =>
                {
                    _searchDebounce.Stop();
                    ApplySearch(_pendingSearch);
                };
            }

            // Restart the timer so search only fires 250 ms after the user stops typing
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }

        private async void ApplySearch(string text)
        {
            if (_chats == null) return;
            var lower = text.ToLowerInvariant();

            // Run LINQ filtering on a background thread; only bind the result on the UI thread
            var results = await Task.Run(() =>
                string.IsNullOrEmpty(lower)
                    ? _chats.OrderByDescending(c => c.Selected).ToList()
                    : _chats
                        .Where(c =>
                            c.NameLower.Contains(lower) ||
                            c.Id.ToString().Contains(lower) ||
                            c.UsernameLower.Contains(lower) ||
                            c.Type.Contains(lower, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(c => c.Selected)
                        .ToList());

            ItemsListView.ItemsSource = results;
            tbCountChats.Text = results.Count.ToString();
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

        private void BtnAppVersion_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingRelease == null) return;
            var dlg = new UpdateDialog(_pendingRelease) { Owner = this };
            dlg.ShowDialog();
            if (dlg.WasSkipped)
                File.WriteAllText(AppPaths.SkippedVersionFile, _pendingRelease.Version);
            else
                try { File.Delete(AppPaths.SkippedVersionFile); } catch { }
        }

        private async Task CheckForAppUpdateAsync()
        {
            try
            {
                var release = await AutoUpdateService.CheckAsync();
                if (release == null) return;

                var skippedVersion = File.Exists(AppPaths.SkippedVersionFile)
                    ? File.ReadAllText(AppPaths.SkippedVersionFile).Trim()
                    : string.Empty;
                if (skippedVersion == release.Version) return;

                _pendingRelease = release;

                Dispatcher.Invoke(() =>
                {
                    btnAppVersion.ToolTip = $"New version {release.Version} available — click to update!";
                    btnAppVersion.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(76, 175, 80));

                    var storyboard = (System.Windows.Media.Animation.Storyboard)FindResource("BlinkVersion");
                    storyboard.Begin();
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Update check failed");
            }
        }


        protected override void OnClosing(CancelEventArgs e)
        {
            // Minimize to tray instead of closing
            e.Cancel = true;
            Hide();
            App.TrayIcon?.ShowBalloonTip(2000, "Still running",
                "Telegram Auto Download is running in the system tray.", System.Windows.Forms.ToolTipIcon.Info);
        }
    }
}
