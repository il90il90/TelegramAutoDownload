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
        private IList<ChatDto> _chats;

        public MainWindow(TelegramApp telegram, ConfigFile config)
        {
            InitializeComponent();

            TelegramApp = telegram;
            ConfigFile = config;
            Loaded += MainWindow_Loaded;

            var notification = new Notification();
            telegram.OnSaved = notification.OnUpdateResultMessageAsync;
            telegram.OnWarnningMessage = notification.OnWarnningMessageAsync;

            // Bind active downloads panel
            dgDownloads.ItemsSource = DownloadProgressService.Instance.Downloads;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await Task.Delay(500);
            await InitAsync();
        }

        private async Task InitAsync()
        {
            await LoadDataAsync();
            ConfigParams configParams = ConfigFile.Read();
            if (configParams?.Chats == null) return;
            tbCountChats.Text = _chats.Count.ToString();

            TelegramApp.UpdateConfig(configParams);
            UpdatePathOnUI(configParams.PathSaveFile);

            // Restore threads slider value
            threadsSlider.Value = Math.Max(1, Math.Min(10, configParams.DownloadThreads));
        }

        private async Task LoadDataAsync()
        {
            try
            {
                ConfigParams configParams = ConfigFile.Read();
                _chats = await TelegramApp.GetAllChats();

                foreach (var chat in _chats)
                {
                    var fromConfigFile = configParams.Chats?.FirstOrDefault(a => a.Id == chat.Id);
                    if (fromConfigFile == null) continue;

                    chat.Selected = fromConfigFile.Selected;
                    chat.ReactionIcon = fromConfigFile.ReactionIcon;
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
                    ItemsListView.ItemsSource = _chats.OrderByDescending(a => a.Selected);
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            hlOpenFolder.Inlines.Clear();
            hlOpenFolder.Inlines.Add(new Run(path));
            hlOpenFolder.IsEnabled = true;
        }

        private void ThreadsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (tbThreads == null) return;
            int threads = (int)e.NewValue;
            tbThreads.Text = threads.ToString();

            var config = ConfigFile.Read();
            config.DownloadThreads = threads;
            ConfigFile.Save(config);
            TelegramApp.UpdateConfig(config);
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
                if (hlOpenFolder.Inlines.FirstOrDefault() is Run run)
                {
                    Process.Start("explorer.exe", run.Text);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
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
            var comboBox = sender as ComboBox;
            if (comboBox != null)
            {
                DependencyObject parent = VisualTreeHelper.GetParent(comboBox);
                while (parent is not ListViewItem && parent != null)
                {
                    parent = VisualTreeHelper.GetParent(parent);
                }

                if (parent is ListViewItem listViewItem)
                {
                    var chatDto = listViewItem.DataContext as ChatDto;
                    if (chatDto != null)
                    {
                        comboBox.Text = chatDto.ReactionIcon;
                    }
                }
            }
        }

        private void Download_Checked(object sender, RoutedEventArgs e)
        {
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
            var checkbox = sender as CheckBox;
            if (checkbox != null)
            {
                var chatDto = checkbox.DataContext as ChatDto;
                switch (checkbox.Content)
                {
                    case "Videos":
                        checkbox.IsChecked = chatDto?.Download.Videos;
                        break;
                    case "Photos":
                        checkbox.IsChecked = chatDto?.Download.Photos;
                        break;
                    case "Music":
                        checkbox.IsChecked = chatDto?.Download.Music;
                        break;
                    case "Files":
                        checkbox.IsChecked = chatDto?.Download.Files;
                        break;
                    default:
                        break;
                }
            }
        }

        private void Provider_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                var chatDto = checkBox.DataContext as ChatDto;
                var pluginName = checkBox.Tag as string ?? string.Empty;
                if (chatDto == null) return;

                // Missing key = enabled by default
                if (chatDto.EnabledPlugins.TryGetValue(pluginName, out var enabled))
                    checkBox.IsChecked = enabled;
                else
                    checkBox.IsChecked = true;
            }
        }

        private void Provider_Checked(object sender, RoutedEventArgs e)
        {
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
