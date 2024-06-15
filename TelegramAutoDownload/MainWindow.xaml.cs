﻿using Microsoft.Win32;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using TelegramAutoDownload.Models;
using TelegramClient;
using TelegramClient.Models;
using TL;
namespace TelegramAutoDownload
{
    public partial class MainWindow : Window
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
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await Task.Delay(500);
            await LoadDataAsync();
            ItemsListView.IsEnabled = true;
            Init();
        }

        private void Init()
        {
            ConfigParams configParams = ConfigFile.Read();
            if (configParams?.Chats == null) return;
            TelegramApp.UpdateConfig(configParams);
            UpdatePathOnUI(configParams.PathSaveFile);
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
                    if (fromConfigFile != null)
                    {
                        chat.Selected = fromConfigFile.Selected;
                        chat.ReactionIcon = fromConfigFile.ReactionIcon;
                    }
                }

                ItemsListView.ItemsSource = _chats.OrderByDescending(a => a.Selected);

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
            hlOpenFolder.Inlines.Clear();
            hlOpenFolder.Inlines.Add(new Run(path));
            hlOpenFolder.IsEnabled = true;
            tbCountChats.Text = _chats.Count.ToString();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBox? textBox = sender as TextBox;
            string textSearch = textBox.Text.ToLower();
            if (_chats == null) return;

            var chats = _chats.Cast<ChatDto>().Where(c => c.Name.ToLower().Contains(textSearch) ||
            c.Username != null && c.Username.Contains(textSearch.ToLower()) || c.Type.ToLower().Contains(textSearch.ToLower())).OrderByDescending(a => a.Selected);
            ItemsListView.ItemsSource = chats;
            tbCountChats.Text = chats.Count().ToString();
        }

        private void SelectChatId_Checked(object sender, RoutedEventArgs e)
        {
            if (ItemsListView.ItemsSource == null)
                return;
            var checkedItems = _chats.Cast<ChatDto>().Where(item => item.Selected).ToList();

            ConfigParams configParams = ConfigFile.Read();
            configParams.Chats = checkedItems;
            ConfigFile.Save(configParams);

            TelegramApp.UpdateConfig(configParams);
        }

        private void HlOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = ((Run)hlOpenFolder.Inlines.FirstOrDefault()).Text;
                Process.Start("explorer.exe", path);
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
                    };

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
                while (!(parent is ListViewItem) && parent != null)
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
    }
}