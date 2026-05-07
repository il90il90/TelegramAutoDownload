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
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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

        // Sorted view for the downloads DataGrid — keeps active items at top
        private ICollectionView? _downloadsView;

        // Suppress event handlers while loading data to prevent redundant file I/O
        private bool _isLoading = false;

        // Holds the pending release when an update is available (shown via blink)
        private ReleaseInfo? _pendingRelease;

        // Guard against double-click while a manual update check is in progress
        private bool _checkingForUpdate = false;

        // Debounce timer for search — avoids hammering LINQ on every keystroke
        private System.Windows.Threading.DispatcherTimer? _searchDebounce;
        private string _pendingSearch = string.Empty;

        // Column sort state for the chat ListView
        private GridViewColumnHeader? _lastSortHeader;
        private string? _lastSortProperty;
        private ListSortDirection _lastSortDirection = ListSortDirection.Ascending;

        // Maps column header display text → ChatDto property name for sorting
        private static readonly Dictionary<string, string> _headerToSortProperty = new()
        {
            { "ID",       nameof(ChatDto.Id) },
            { "Name",     nameof(ChatDto.Name) },
            { "Username", nameof(ChatDto.Username) },
            { "Type",     nameof(ChatDto.Type) },
            { "Members",  nameof(ChatDto.MembersCount) },
        };

        public MainWindow(TelegramApp telegram, ConfigFile config)
        {
            InitializeComponent();

            TelegramApp = telegram;
            ConfigFile = config;
            Loaded += MainWindow_Loaded;

            var configParams = config.Read();
            _notification = new Notification(configParams);
            telegram.OnSaved = _notification.OnUpdateResultMessageAsync;
            telegram.OnWarnningMessage = async eventMsg =>
            {
                // Propagate error message to the download row so the UI shows it as a tooltip
                var r = eventMsg.ResultExecute;
                var lookupName = !string.IsNullOrEmpty(r.NotificationKey) ? r.NotificationKey : r.FileName;
                if (!string.IsNullOrEmpty(lookupName) && !string.IsNullOrEmpty(r.ErrorMessage))
                    DownloadProgressService.Instance.SetDownloadError(eventMsg.Chat.Name, lookupName, r.ErrorMessage);
                return await _notification.OnWarnningMessageAsync(eventMsg);
            };

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
            telegram.OnSkipped = (chatName, msgId) =>
                DownloadProgressService.Instance.SkipDownload(chatName, msgId);

            // Wire retry callback so the UI can show a Retry button on failed items
            telegram.OnRetryReady = (chatName, fileName, retryAction) =>
                DownloadProgressService.Instance.SetRetryAction(chatName, fileName, retryAction);

            // Wire history entries: append each incoming message to the chat's JSONL file
            telegram.OnHistoryEntry = (chat, entry) =>
            {
                var basePath = ConfigFile.Read().PathSaveFile;
                if (string.IsNullOrWhiteSpace(basePath)) return;
                _ = TelegramClient.ChatHistoryService.AppendEntryAsync(
                        chat.Type ?? "Other", chat.Name, entry, basePath);
            };

            // Enhanced completion notification with size, duration, avg speed
            DownloadProgressService.Instance.DownloadCompleted += (chatName, fileName, bytes, duration) =>
                _ = _notification.OnDownloadCompletedAsync(chatName, fileName, bytes, duration);

            // Bind active downloads panel through a sorted CollectionView so active downloads stay at top
            _downloadsView = CollectionViewSource.GetDefaultView(DownloadProgressService.Instance.Downloads);
            _downloadsView.SortDescriptions.Add(new SortDescription(nameof(DownloadItem.SortOrder), ListSortDirection.Ascending));
            _downloadsView.SortDescriptions.Add(new SortDescription(nameof(DownloadItem.StartTime), ListSortDirection.Descending));
            dgDownloads.ItemsSource = _downloadsView;

            DownloadProgressService.Instance.Downloads.CollectionChanged += (_, __) =>
            {
                UpdateDownloadBadges();
                UpdateStatsStrip();
            };
            DownloadProgressService.Instance.StatsChanged += UpdateStatsStrip;
            DownloadProgressService.Instance.QueueChanged += UpdateDownloadBadges;

            // Bootstrap StatisticsService (singleton) and refresh all-time counters whenever they change
            StatisticsService.Instance.Changed += UpdateStatsStrip;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            btnAppVersion.Content = $"v{AppVersion.Current}";

            // Step 1: Show saved chats from last session immediately — no network call needed
            LoadChatsFromConfig();

            // Step 2: Show a lightweight refresh indicator instead of a full blocking overlay
            mainLoadingRing.IsActive = true;
            tbLoadingStatus.Text = "Refreshing chats...";

            // Step 3: Ensure yt-dlp silently in background
            _ = YtdlpService.EnsureAsync();

            // Step 4: Connect to Telegram and fetch fresh chats in background
            await Task.Run(() => TelegramApp.WaitForLoginAsync(15000));
            await RefreshChatsFromTelegramAsync();

            mainLoadingRing.IsActive = false;
            tbLoadingStatus.Text = string.Empty;

            // Step 5: Apply saved config (UpdateConfig, path, notifications)
            ConfigParams configParams = ConfigFile.Read();
            TelegramApp.UpdateConfig(configParams);
            UpdatePathOnUI(configParams.PathSaveFile);

            var monitoredCount = configParams.Chats?.Count(c => c.Selected) ?? 0;
            _ = Task.Run(async () =>
            {
                await TelegramApp.WaitForLoginAsync();
                await _notification.SendStartupNotificationAsync(monitoredCount, TelegramApp.Client.UserId != 0);
            });

            // Step 6: Check for updates after window is ready
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000);
                await CheckForAppUpdateAsync();
            });
        }

        /// <summary>
        /// Loads the chat list instantly from the last saved config (no network calls).
        /// Shows the chats the user expects to see right away on startup.
        /// </summary>
        private void LoadChatsFromConfig()
        {
            try
            {
                ConfigParams configParams = ConfigFile.Read();
                if (configParams?.Chats == null || configParams.Chats.Count == 0) return;

                var chats = configParams.Chats.Select(c => new ChatDto
                {
                    Id              = c.Id,
                    Name            = c.Name,
                    Username        = c.Username,
                    Type            = c.Type ?? string.Empty,
                    NameLower       = c.Name?.ToLowerInvariant() ?? string.Empty,
                    UsernameLower   = c.Username?.ToLowerInvariant() ?? string.Empty,
                    Selected        = c.Selected,
                    ReactionIcon    = c.ReactionIcon,
                    DownloadStartIcon = c.DownloadStartIcon,
                    Download        = c.Download ?? new Download(),
                    DownloadFromSize = c.DownloadFromSize,
                    IgnoreFileByRegex = c.IgnoreFileByRegex,
                    EnabledPlugins  = c.EnabledPlugins ?? new Dictionary<string, bool>(),
                    YtdlpQuality    = string.IsNullOrEmpty(c.YtdlpQuality) ? "best" : c.YtdlpQuality,
                    FolderTemplate  = c.FolderTemplate ?? string.Empty,
                    SaveHistory     = c.SaveHistory,
                    HistoryIcon     = c.HistoryIcon ?? string.Empty,
                    MembersCount    = c.MembersCount,
                    Muted           = c.Muted,
                }).ToList();

                _isLoading = true;
                _chats = chats;
                ItemsListView.ItemsSource = _chats.OrderByDescending(a => a.Selected).ToList();
                ReapplyColumnSort();
                _isLoading = false;
                tbCountChats.Text = _chats.Count.ToString();
            }
            catch { /* if config is missing or corrupt, just show an empty list */ }
        }

        /// <summary>
        /// Fetches fresh chat data from Telegram in the background and merges saved settings into the result.
        /// Updates the UI once the refresh is complete.
        /// </summary>
        private async Task RefreshChatsFromTelegramAsync()
        {
            try
            {
                ConfigParams configParams = ConfigFile.Read();

                var freshChats = await Task.Run(async () =>
                {
                    var chats = await TelegramApp.GetAllChats();
                    foreach (var chat in chats)
                    {
                        // Pre-compute lowercase strings once so search never allocates on every keystroke
                        chat.NameLower = chat.Name?.ToLowerInvariant() ?? string.Empty;
                        chat.UsernameLower = chat.Username?.ToLowerInvariant() ?? string.Empty;

                        var saved = configParams.Chats?.FirstOrDefault(a => a.Id == chat.Id);
                        if (saved == null) continue;

                        chat.Selected = saved.Selected;
                        chat.ReactionIcon = saved.ReactionIcon;
                        chat.DownloadStartIcon = saved.DownloadStartIcon;
                        if (saved.Download != null)
                        {
                            chat.Download.Videos = saved.Download.Videos;
                            chat.Download.Photos = saved.Download.Photos;
                            chat.Download.Music = saved.Download.Music;
                            chat.Download.Files = saved.Download.Files;
                        }
                        chat.DownloadFromSize   = saved.DownloadFromSize;
                        chat.IgnoreFileByRegex  = saved.IgnoreFileByRegex;
                        chat.EnabledPlugins     = saved.EnabledPlugins ?? new Dictionary<string, bool>();
                        chat.YtdlpQuality       = string.IsNullOrEmpty(saved.YtdlpQuality) ? "best" : saved.YtdlpQuality;
                        chat.FolderTemplate     = saved.FolderTemplate ?? string.Empty;
                        chat.SaveHistory        = saved.SaveHistory;
                        chat.HistoryIcon        = saved.HistoryIcon ?? string.Empty;
                        chat.Muted              = saved.Muted;
                        // MembersCount is populated from the live Telegram API data — no need to restore from config
                    }
                    return chats;
                });

                _isLoading = true;
                _chats = freshChats;
                ItemsListView.ItemsSource = _chats.OrderByDescending(a => a.Selected).ToList();
                ReapplyColumnSort();
                _isLoading = false;
                tbCountChats.Text = _chats.Count.ToString();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshChatsFromTelegramAsync error: {ex.Message}");
            }
        }

        private async void BtnRefreshChats_Click(object sender, RoutedEventArgs e)
        {
            BtnRefreshChats.IsEnabled = false;
            BtnRefreshChats.Content = "⏳ Loading…";
            await RefreshChatsFromTelegramAsync();
            BtnRefreshChats.IsEnabled = true;
            BtnRefreshChats.Content = "🔄 Refresh";
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

            // Guard: at least one download type must be selected
            var dl = chat.Download;
            if (!dl.Videos && !dl.Photos && !dl.Music && !dl.Files)
            {
                MessageBox.Show("No download types selected.\nPlease check at least one type (Videos, Photos, Music, or Files) before syncing.",
                    "Sync", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirmSync = MessageBox.Show(
                "Syncing all history can take a long time for large chats — " +
                "Telegram allows fetching up to 100 messages per request.\n\n" +
                "The app will remain responsive and you can see progress at the bottom.\n\n" +
                "Continue?",
                "Sync — this may take a while",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (confirmSync != MessageBoxResult.Yes) return;

            btn.IsEnabled = false;
            btn.Content = "…";

            await TelegramApp.SyncHistoryAsync(chat, msg =>
                Dispatcher.InvokeAsync(() => tbLoadingStatus.Text = msg));

            btn.IsEnabled = true;
            btn.Content = "⬇ Sync";
            tbLoadingStatus.Text = string.Empty;
        }

        private async void BtnMuteChat_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ChatDto chat) return;

            btn.IsEnabled = false;
            try
            {
                bool newMuted = !chat.Muted;
                await TelegramApp.MuteChatAsync(chat, newMuted);

                // Update model and persist
                chat.Muted = newMuted;
                var config = ConfigFile.Read();
                var saved = config.Chats.FirstOrDefault(c => c.Id == chat.Id);
                if (saved != null)
                {
                    saved.Muted = newMuted;
                    ConfigFile.Save(config);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not change mute state: {ex.Message}", "Mute",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(ConfigFile, TelegramApp) { Owner = this };
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
                int active = all.Count(d => d.Status == "⬇ Downloading");

                tbDownloadCount.Text = active.ToString();
                tbQueueCount.Text = queued.ToString();
                queueCountBadge.Visibility = queued > 0
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed;

                // Total speed badge — only shown while downloads are active
                var speed = DownloadProgressService.Instance.GetTotalCurrentSpeed();
                if (!string.IsNullOrEmpty(speed))
                {
                    tbTotalSpeed.Text = speed;
                    totalSpeedBadge.Visibility = System.Windows.Visibility.Visible;
                }
                else
                {
                    totalSpeedBadge.Visibility = System.Windows.Visibility.Collapsed;
                }

                // Refresh sort so newly completed items move to the bottom
                _downloadsView?.Refresh();
            });
        }

        private void UpdateStatsStrip()
        {
            Dispatcher.InvokeAsync(() =>
            {
                var svc = DownloadProgressService.Instance;
                tbStatsFiles.Text  = $"{svc.TotalFilesDownloaded} file{(svc.TotalFilesDownloaded == 1 ? "" : "s")}";
                tbStatsBytes.Text  = FormatBytes(svc.TotalBytesDownloaded);
                tbStatsActive.Text = $"{svc.Downloads.Count} active";

                var stats = StatisticsService.Instance;
                tbAllTimeFiles.Text = $"{stats.TotalFilesAllTime:N0} files";
                tbAllTimeBytes.Text = FormatBytes(stats.TotalBytesAllTime);
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
                            (c.Type?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false))
                        .OrderByDescending(c => c.Selected)
                        .ToList());

            ItemsListView.ItemsSource = results;
            ReapplyColumnSort();
            tbCountChats.Text = results.Count.ToString();
        }

        /// <summary>
        /// Handles a click on a GridViewColumnHeader. Looks up the sort property from the header text,
        /// toggles direction when the same column is clicked twice, updates the ▲/▼ indicator,
        /// and applies SortDescriptions to the current ItemsSource view.
        /// </summary>
        private void ColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not GridViewColumnHeader header) return;

            // Strip any existing arrow suffix to get the base header text for the lookup
            var baseText = (header.Column?.Header as string ?? string.Empty).Trim();
            if (!_headerToSortProperty.TryGetValue(baseText, out var sortProperty)) return;

            var direction = (header == _lastSortHeader && _lastSortDirection == ListSortDirection.Ascending)
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            ApplyColumnSort(sortProperty, direction, header, baseText);
        }

        /// <summary>
        /// Applies a SortDescription to the chat list view and updates the column header arrow indicator.
        /// Selected chats are always kept first as a primary sort; the user-chosen column is secondary.
        /// </summary>
        private void ApplyColumnSort(string property, ListSortDirection direction,
                                     GridViewColumnHeader header, string baseHeaderText)
        {
            // Reset arrow on the previously sorted header
            if (_lastSortHeader != null && _lastSortHeader != header)
                _lastSortHeader.Content = _lastSortHeader.Column?.Header;

            // Show arrow on the newly sorted header
            header.Content = $"{baseHeaderText} {(direction == ListSortDirection.Ascending ? "▲" : "▼")}";

            _lastSortHeader = header;
            _lastSortProperty = property;
            _lastSortDirection = direction;

            ReapplyColumnSort();
        }

        /// <summary>
        /// Re-applies the active column sort after the ItemsSource is replaced (refresh, search).
        /// Safe to call even when no sort is active.
        /// </summary>
        private void ReapplyColumnSort()
        {
            if (string.IsNullOrEmpty(_lastSortProperty)) return;
            var view = CollectionViewSource.GetDefaultView(ItemsListView.ItemsSource);
            if (view == null) return;

            view.SortDescriptions.Clear();
            // Primary: selected/monitored chats always float to the top
            view.SortDescriptions.Add(new SortDescription(nameof(ChatDto.Selected), ListSortDirection.Descending));
            // Secondary: user-chosen column sort
            view.SortDescriptions.Add(new SortDescription(_lastSortProperty, _lastSortDirection));
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
                        // Sync all user-configurable fields back to the UI row so nothing goes stale
                        chat.Download           = existingChat.Download ?? chat.Download;
                        chat.ReactionIcon       = existingChat.ReactionIcon;
                        chat.DownloadStartIcon  = existingChat.DownloadStartIcon;
                        chat.DownloadFromSize   = existingChat.DownloadFromSize;
                        chat.IgnoreFileByRegex  = existingChat.IgnoreFileByRegex ?? chat.IgnoreFileByRegex;
                        chat.EnabledPlugins     = existingChat.EnabledPlugins ?? chat.EnabledPlugins;
                        chat.YtdlpQuality       = string.IsNullOrEmpty(existingChat.YtdlpQuality) ? "best" : existingChat.YtdlpQuality;
                        chat.FolderTemplate     = existingChat.FolderTemplate ?? string.Empty;
                        chat.SaveHistory        = existingChat.SaveHistory;
                        chat.HistoryIcon        = existingChat.HistoryIcon ?? string.Empty;
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

        private void CancelAllDownloads_Click(object sender, RoutedEventArgs e)
        {
            DownloadProgressService.Instance.CancelAllDownloads();
        }

        // Default emoji set shown before group reactions are loaded from Telegram
        private static readonly List<string> _defaultEmojiSet = new()
        {
            string.Empty, "👍", "❤️", "🔥", "👌", "💯", "😂", "😮", "🎉"
        };

        /// <summary>
        /// Walks up the visual tree from a ComboBox to find its parent ListViewItem
        /// and returns the ChatDto bound to that row.
        /// </summary>
        private static ChatDto? GetChatDtoFromComboBox(ComboBox comboBox)
        {
            DependencyObject parent = VisualTreeHelper.GetParent(comboBox);
            while (parent is not ListViewItem && parent != null)
                parent = VisualTreeHelper.GetParent(parent);
            return (parent as ListViewItem)?.DataContext as ChatDto;
        }

        /// <summary>
        /// Populates a ComboBox with the reactions available for its chat.
        /// Uses cached reactions when available; falls back to the default set.
        /// The current saved value is always included in the list even if the group removed it.
        /// </summary>
        private void SetupEmojiComboBox(ComboBox comboBox, ChatDto chatDto, string currentValue)
        {
            _isLoading = true;
            try
            {
                var baseItems = chatDto.AvailableReactions ?? _defaultEmojiSet;
                var items = new List<string>(baseItems);

                // Ensure the empty "no reaction" option is always first
                if (!items.Contains(string.Empty))
                    items.Insert(0, string.Empty);

                // Keep the previously saved value visible even if not in the group's reaction set
                if (!string.IsNullOrEmpty(currentValue) && !items.Contains(currentValue))
                    items.Insert(1, currentValue);

                comboBox.ItemsSource = items;
                comboBox.SelectedItem = currentValue;
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// Updates the Visibility of the Start and End icon ComboBoxes in the same row.
        /// Hidden when the chat's available reactions list is known to be empty (reactions disabled).
        /// </summary>
        private static void UpdateIconComboBoxVisibility(ChatDto chatDto, ListViewItem? row)
        {
            if (row == null) return;
            var visibility = chatDto.AvailableReactions?.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;

            // Walk the visual tree to find all icon ComboBoxes in this row
            foreach (var combo in FindVisualChildren<ComboBox>(row))
            {
                var tag = combo.Tag as string;
                if (tag == "start" || tag == "end" || tag == "history")
                    combo.Visibility = visibility;
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed)
                    yield return typed;
                foreach (var descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }

        private static ListViewItem? GetListViewItemFromComboBox(ComboBox comboBox)
        {
            DependencyObject parent = VisualTreeHelper.GetParent(comboBox);
            while (parent is not ListViewItem && parent != null)
                parent = VisualTreeHelper.GetParent(parent);
            return parent as ListViewItem;
        }

        private void DownloadStartIcon_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox comboBox) return;
            var chatDto = GetChatDtoFromComboBox(comboBox);
            if (chatDto == null) return;
            SetupEmojiComboBox(comboBox, chatDto, chatDto.DownloadStartIcon);

            // Hide immediately if reactions are already known to be disabled
            if (chatDto.AvailableReactions?.Count == 0)
                comboBox.Visibility = Visibility.Collapsed;
        }

        private void EndIconComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox comboBox) return;
            var chatDto = GetChatDtoFromComboBox(comboBox);
            if (chatDto == null) return;
            SetupEmojiComboBox(comboBox, chatDto, chatDto.ReactionIcon);

            // Hide immediately if reactions are already known to be disabled
            if (chatDto.AvailableReactions?.Count == 0)
                comboBox.Visibility = Visibility.Collapsed;
        }

        private void HistoryIconComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox comboBox) return;
            var chatDto = GetChatDtoFromComboBox(comboBox);
            if (chatDto == null) return;
            SetupEmojiComboBox(comboBox, chatDto, chatDto.HistoryIcon);

            if (chatDto.AvailableReactions?.Count == 0)
                comboBox.Visibility = Visibility.Collapsed;
        }

        private void HistoryIconComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not ComboBox comboBox || comboBox.SelectedItem == null) return;

            var icon = comboBox.SelectedItem as string ?? string.Empty;
            var dataContext = comboBox.DataContext as ChatDto;
            if (dataContext == null) return;

            var config = ConfigFile.Read();
            var foundChat = config.Chats.FirstOrDefault(a => a.Id == dataContext.Id);
            if (foundChat == null) return;

            dataContext.HistoryIcon = icon;
            foundChat.HistoryIcon = icon;
            ConfigFile.Save(config);
            TelegramApp.UpdateConfig(config);
        }

        /// <summary>
        /// Fetches available reactions from Telegram when the user opens a ComboBox dropdown.
        /// The ComboBox Tag ("start" or "end") identifies which icon field to restore after loading.
        /// Updates happen live in the open dropdown.
        /// </summary>
        private async void EmojiComboBox_DropDownOpened(object sender, EventArgs e)
        {
            if (sender is not ComboBox comboBox) return;
            var chatDto = GetChatDtoFromComboBox(comboBox);
            if (chatDto == null) return;

            // Already loaded for this chat — nothing to do
            if (chatDto.AvailableReactions != null) return;

            try
            {
                var reactions = await TelegramApp.GetChatAvailableReactionsAsync(chatDto);
                chatDto.AvailableReactions = reactions;

                if (!comboBox.IsLoaded) return;

                var tag = comboBox.Tag as string;
                var currentValue = tag == "start"   ? chatDto.DownloadStartIcon
                                 : tag == "history" ? chatDto.HistoryIcon
                                 : chatDto.ReactionIcon;

                SetupEmojiComboBox(comboBox, chatDto, currentValue);

                // Update visibility for all icon ComboBoxes in the same row
                var row = GetListViewItemFromComboBox(comboBox);
                UpdateIconComboBoxVisibility(chatDto, row);
            }
            catch { /* non-critical */ }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not ComboBox comboBox || comboBox.SelectedItem == null) return;

            var reactionIcon = comboBox.SelectedItem as string ?? string.Empty;
            var dataContext = comboBox.DataContext as ChatDto;
            if (dataContext == null) return;

            var config = ConfigFile.Read();
            var foundChat = config.Chats.FirstOrDefault(a => a.Id == dataContext.Id);
            if (foundChat == null)
            {
                MessageBox.Show($"Please select a {dataContext.Type} before choosing an End Icon.", "",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            dataContext.ReactionIcon = reactionIcon;
            foundChat.ReactionIcon = reactionIcon;

            ConfigFile.Save(config);
            TelegramApp.UpdateConfig(config);
        }

        private void DownloadStartIcon_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not ComboBox comboBox || comboBox.SelectedItem == null) return;

            var icon = comboBox.SelectedItem as string ?? string.Empty;
            var dataContext = comboBox.DataContext as ChatDto;
            if (dataContext == null) return;

            var config = ConfigFile.Read();
            var foundChat = config.Chats.FirstOrDefault(a => a.Id == dataContext.Id);
            if (foundChat == null)
            {
                MessageBox.Show("Please select the chat first.", "", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            dataContext.DownloadStartIcon = icon;
            foundChat.DownloadStartIcon = icon;
            ConfigFile.Save(config);
            TelegramApp.UpdateConfig(config);
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

        private void ProviderPill_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton pill) return;
            var chatDto   = pill.DataContext as ChatDto;
            var pluginName = pill.Tag as string ?? string.Empty;
            if (chatDto == null) return;

            _isLoading = true;
            // Missing key = disabled. User must explicitly enable each plugin per chat.
            pill.IsChecked = chatDto.EnabledPlugins.TryGetValue(pluginName, out var val) && val;
            _isLoading = false;
        }

        private void ProviderPill_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not ToggleButton pill) return;

            var chatDto    = pill.DataContext as ChatDto;
            var pluginName = pill.Tag as string ?? string.Empty;
            if (chatDto == null || string.IsNullOrEmpty(pluginName)) return;

            var isEnabled = pill.IsChecked == true;
            chatDto.EnabledPlugins[pluginName] = isEnabled;

            var config    = ConfigFile.Read();
            var foundChat = config.Chats.FirstOrDefault(a => a.Id == chatDto.Id);
            if (foundChat != null)
            {
                foundChat.EnabledPlugins[pluginName] = isEnabled;
                ConfigFile.Save(config);
                TelegramApp.UpdateConfig(config);
            }
        }

        private void QualityComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox comboBox) return;
            var chatDto = comboBox.DataContext as ChatDto;
            if (chatDto == null) return;

            _isLoading = true;
            comboBox.ItemsSource = BasePlugins.YtdlpFormatHelper.QualityOptions;
            var saved = chatDto.YtdlpQuality;
            comboBox.SelectedItem = BasePlugins.YtdlpFormatHelper.QualityOptions.Contains(saved) ? saved : "best";
            _isLoading = false;
        }

        private void QualityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not ComboBox comboBox || comboBox.SelectedItem is not string quality) return;

            var chatDto = comboBox.DataContext as ChatDto;
            if (chatDto == null) return;

            var config = ConfigFile.Read();
            var foundChat = config.Chats.FirstOrDefault(a => a.Id == chatDto.Id);
            if (foundChat == null) return;

            chatDto.YtdlpQuality = quality;
            foundChat.YtdlpQuality = quality;
            ConfigFile.Save(config);
            TelegramApp.UpdateConfig(config);
        }

        // ── Filter (IgnoreFileByRegex) ────────────────────────────────────────────────

        /// <summary>Opens the rich Filter Editor dialog for the chat on the clicked row.</summary>
        private void FilterDialog_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ChatDto chat) return;

            var config   = ConfigFile.Read();
            var basePath = config.PathSaveFile ?? string.Empty;

            var dlg = new FilterDialog(
                currentPatterns: chat.IgnoreFileByRegex,
                chatName:        chat.Name,
                chatType:        chat.Type ?? "Other",
                basePath:        basePath,
                fetchMessages:   () => TelegramApp.GetRecentMessagesAsync(chat, 50))
            {
                Owner = this
            };

            if (dlg.ShowDialog() != true) return;

            chat.IgnoreFileByRegex = dlg.ResultPatterns;

            var found = config.Chats.FirstOrDefault(c => c.Id == chat.Id);
            if (found != null)
            {
                found.IgnoreFileByRegex = dlg.ResultPatterns;
                ConfigFile.Save(config);
                TelegramApp.UpdateConfig(config);
            }

            ItemsListView.Items.Refresh();
        }

        // ── Folder Template ───────────────────────────────────────────────────────────

        private void FolderTemplateTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is ChatDto chat)
                tb.Text = chat.FolderTemplate;
        }

        private void FolderTemplateTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb || tb.DataContext is not ChatDto chat) return;

            chat.FolderTemplate = tb.Text.Trim();

            var config = ConfigFile.Read();
            var found = config.Chats.FirstOrDefault(c => c.Id == chat.Id);
            if (found != null)
            {
                found.FolderTemplate = chat.FolderTemplate;
                ConfigFile.Save(config);
                TelegramApp.UpdateConfig(config);
            }
        }

        /// <summary>Opens the guided Folder Template dialog for the chat on the clicked row.</summary>
        private void FolderTemplateDialog_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ChatDto chat) return;

            var config   = ConfigFile.Read();
            var basePath = config.PathSaveFile ?? string.Empty;

            var dlg = new FolderTemplateDialog(
                currentTemplate: chat.FolderTemplate,
                chatName:        chat.Name,
                chatType:        chat.Type ?? "Other",
                basePath:        basePath)
            {
                Owner = this
            };

            if (dlg.ShowDialog() != true) return;

            // Persist the new template
            chat.FolderTemplate = dlg.ResultTemplate;

            var found = config.Chats.FirstOrDefault(c => c.Id == chat.Id);
            if (found != null)
            {
                found.FolderTemplate = dlg.ResultTemplate;
                ConfigFile.Save(config);
                TelegramApp.UpdateConfig(config);
            }

            // Refresh row so the TextBox shows the updated value
            ItemsListView.Items.Refresh();
        }

        // ── Members export ───────────────────────────────────────────────────────────

        private void MembersExport_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ChatDto chat) return;
            if (TelegramApp == null)
            {
                MessageBox.Show("Telegram is not connected yet.", "Not connected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new MembersExportDialog(TelegramApp, chat) { Owner = this };
            dlg.Show();
        }

        // ── History (SaveHistory toggle + Export) ────────────────────────────────────

        private void HistoryCheckBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is ChatDto chat)
                cb.IsChecked = chat.SaveHistory;
        }

        private void HistoryCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.DataContext is not ChatDto chat) return;

            chat.SaveHistory = cb.IsChecked == true;

            var config = ConfigFile.Read();
            var found = config.Chats.FirstOrDefault(c => c.Id == chat.Id);
            if (found != null)
            {
                found.SaveHistory = chat.SaveHistory;
                ConfigFile.Save(config);
                TelegramApp.UpdateConfig(config);
            }
        }

        private async void BtnExportHistory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ChatDto chat) return;
            if (!chat.Selected)
            {
                MessageBox.Show(
                    "Enable monitoring for this chat first, then export its history.",
                    "Export History", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var basePath = ConfigFile.Read().PathSaveFile;
            if (string.IsNullOrWhiteSpace(basePath))
            {
                MessageBox.Show(
                    "Please set a download folder in Settings before exporting history.",
                    "Export History", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirmExport = MessageBox.Show(
                "Exporting the full chat history can take a long time for large chats — " +
                "Telegram allows fetching up to 100 messages per request.\n\n" +
                "The app will remain responsive and you can see progress at the bottom.\n\n" +
                "Continue?",
                "Export History — this may take a while",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (confirmExport != MessageBoxResult.Yes) return;

            btn.IsEnabled = false;
            btn.Content   = "…";

            await TelegramApp.ExportChatHistoryAsync(chat, basePath,
                msg => Dispatcher.InvokeAsync(() => tbLoadingStatus.Text = msg));

            btn.IsEnabled = true;
            btn.Content   = "📤";
            tbLoadingStatus.Text = string.Empty;
        }

        // ── Retry ─────────────────────────────────────────────────────────────────────

        private void RetryDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not TelegramAutoDownload.Models.DownloadItem item) return;
            var retry = item.RetryAsync;
            if (retry == null) return;

            // Clear retry state so the button disappears while re-downloading
            item.RetryAsync = null;
            item.Status = "⏳ Queued";
            _ = retry.Invoke();
        }

        private void DownloadSize_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoading) return;
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

        private async void BtnAppVersion_Click(object sender, RoutedEventArgs e)
        {
            // ── Update is already pending ─────────────────────────────────────────
            if (_pendingRelease != null)
            {
                var dlg = new UpdateDialog(_pendingRelease) { Owner = this };
                dlg.ShowDialog();
                if (dlg.WasSkipped)
                    File.WriteAllText(AppPaths.SkippedVersionFile, _pendingRelease.Version);
                else
                    try { File.Delete(AppPaths.SkippedVersionFile); } catch { }
                return;
            }

            // ── No pending update — perform a manual check now ────────────────────
            if (_checkingForUpdate) return; // Prevent concurrent checks
            _checkingForUpdate = true;

            object originalContent  = btnAppVersion.Content;
            object? originalTooltip = btnAppVersion.ToolTip;
            var    originalBrush    = btnAppVersion.Foreground;

            btnAppVersion.IsEnabled = false;
            btnAppVersion.Content   = "Checking...";
            btnAppVersion.ToolTip   = "Checking for updates…";

            try
            {
                var release = await AutoUpdateService.CheckAsync();

                if (release == null)
                {
                    // Already up to date — show brief green feedback, then restore
                    btnAppVersion.Content    = "✓ Up to date";
                    btnAppVersion.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(76, 175, 80));
                    btnAppVersion.ToolTip = "You are running the latest version";

                    await Task.Delay(2500);

                    btnAppVersion.Content    = originalContent;
                    btnAppVersion.Foreground = originalBrush;
                    btnAppVersion.ToolTip    = originalTooltip;
                }
                else
                {
                    // New version found — highlight button and open dialog immediately
                    // (the user explicitly asked to check, so show it even if previously skipped)
                    _pendingRelease = release;

                    btnAppVersion.Content    = originalContent;
                    btnAppVersion.ToolTip    = $"New version {release.Version} available — click to update!";
                    btnAppVersion.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(76, 175, 80));

                    var storyboard = (System.Windows.Media.Animation.Storyboard)FindResource("BlinkVersion");
                    storyboard.Begin();

                    var dlg = new UpdateDialog(_pendingRelease) { Owner = this };
                    dlg.ShowDialog();
                    if (dlg.WasSkipped)
                        File.WriteAllText(AppPaths.SkippedVersionFile, _pendingRelease.Version);
                    else
                        try { File.Delete(AppPaths.SkippedVersionFile); } catch { }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Manual update check failed");
                btnAppVersion.Content    = originalContent;
                btnAppVersion.Foreground = originalBrush;
                btnAppVersion.ToolTip    = "Check failed — click to retry";
                await Task.Delay(2000);
                btnAppVersion.ToolTip = originalTooltip;
            }
            finally
            {
                btnAppVersion.IsEnabled    = true;
                _checkingForUpdate         = false;
            }
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
            // Always cancel the raw close event and show the choice dialog instead
            e.Cancel = true;

            var dlg = new CloseDialog { Owner = this };
            dlg.ShowDialog();

            switch (dlg.Result)
            {
                case CloseAction.MinimizeToTray:
                    Hide();
                    App.TrayIcon?.ShowBalloonTip(2000, "Still running",
                        "Telegram Auto Download is running in the system tray.",
                        System.Windows.Forms.ToolTipIcon.Info);
                    break;

                case CloseAction.Exit:
                    App.TrayIcon?.Dispose();
                    Application.Current.Shutdown();
                    break;

                // CloseAction.Cancel — do nothing, window stays open
            }
        }
    }
}
