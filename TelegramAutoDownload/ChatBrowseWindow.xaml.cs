using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.Controls;
using TelegramClient;
using TelegramClient.Factory.FactoriesMessages.Enum;
using TelegramClient.Models;
using TL;

namespace TelegramAutoDownload
{
    public partial class ChatBrowseWindow : MetroWindow
    {
        private readonly TelegramApp _app;
        private readonly ChatDto _chat;
        private int _nextOffset;
        private bool _hasMore = true;
        private bool _loading;

        public ObservableCollection<ChatBrowseRow> Rows { get; } = new();

        public ChatBrowseWindow(TelegramApp app, ChatDto chat)
        {
            InitializeComponent();
            _app = app;
            _chat = chat;
            var who = string.IsNullOrEmpty(chat.Username) ? chat.Name : $"{chat.Name}  @{chat.Username}";
            tbHeader.Text = $"{who}  ({chat.Type})";
            lvMessages.ItemsSource = Rows;
            Loaded += async (_, _) =>
            {
                // If startup refresh has not cached access hashes yet, wait briefly and retry once.
                await LoadFirstPageAsync();
                if (Rows.Count == 0 && _nextOffset == 0)
                {
                    await Task.Delay(1500);
                    await LoadFirstPageAsync();
                }
            };
        }

        private void RebuildKindQuickSelectButtons()
        {
            panelKindQuickSelect.Children.Clear();
            var kinds = Rows
                .Select(r => r.KindText)
                .Where(k => !string.IsNullOrWhiteSpace(k) && k != "—")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var kind in kinds)
            {
                var k = kind;
                var btn = new Button
                {
                    Content = $"Only {k}",
                    Margin = new Thickness(0, 0, 6, 6),
                    Padding = new Thickness(10, 4, 10, 4),
                    MinHeight = 28,
                    ToolTip = $"Select all downloadable “{k}” rows and clear other checkboxes.",
                };
                btn.Click += (_, _) => SelectOnlyKind(k);
                panelKindQuickSelect.Children.Add(btn);
            }
        }

        private void SelectOnlyKind(string kind)
        {
            foreach (var row in Rows)
                row.IsSelected = row.CanDownload && string.Equals(row.KindText, kind, StringComparison.OrdinalIgnoreCase);
        }

        private void BtnClearSelection_OnClick(object sender, RoutedEventArgs e)
        {
            foreach (var row in Rows)
                row.IsSelected = false;
        }

        private void BtnSelectAllDownloadable_OnClick(object sender, RoutedEventArgs e)
        {
            foreach (var row in Rows)
                row.IsSelected = row.CanDownload;
        }

        private async Task LoadFirstPageAsync()
        {
            Rows.Clear();
            RebuildKindQuickSelectButtons();
            _nextOffset = 0;
            _hasMore = true;
            await LoadPageAsync();
        }

        private async Task LoadPageAsync()
        {
            if (_loading || !_hasMore) return;
            _loading = true;
            btnLoadOlder.IsEnabled = false;
            tbStatus.Text = "Loading…";
            try
            {
                var (page, next, hasMore, error) = await _app.FetchBrowseHistoryPageAsync(_chat, _nextOffset, 40);
                _nextOffset = next;
                _hasMore = hasMore;
                foreach (var m in page.OrderByDescending(x => x.ID))
                    Rows.Add(new ChatBrowseRow(m, _chat));
                RebuildKindQuickSelectButtons();
                if (!string.IsNullOrEmpty(error))
                    tbStatus.Text = error;
                else if (Rows.Count == 0)
                    tbStatus.Text = "No messages in this chat yet, or history is empty.";
                else
                    tbStatus.Text = $"{Rows.Count} messages — use Load older for more history.";
            }
            catch (Exception ex)
            {
                tbStatus.Text = ex is TaskCanceledException
                    ? "Timed out — click Refresh on the main window, then try again (wait if downloads are running)."
                    : ex.Message;
            }
            finally
            {
                _loading = false;
                btnLoadOlder.IsEnabled = _hasMore;
            }
        }

        private async void BtnLoadOlder_OnClick(object sender, RoutedEventArgs e) => await LoadPageAsync();

        private void LvMessages_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lvMessages.SelectedItem is ChatBrowseRow row)
                tbDetail.Text = row.TgMessage.message ?? string.Empty;
            else
                tbDetail.Text = string.Empty;
        }

        private async void BtnDownload_OnClick(object sender, RoutedEventArgs e)
        {
            var selected = Rows.Where(r => r.IsSelected && r.CanDownload).Select(r => r.TgMessage).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "Select at least one downloadable message (checkbox).", "Download",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            btnDownload.IsEnabled = false;
            tbStatus.Text = "Downloading…";
            try
            {
                await _app.ManualDownloadMessagesAsync(_chat, selected, forBrowseWindow: true);
                tbStatus.Text = $"Processed {selected.Count} message(s).";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Download failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnDownload.IsEnabled = true;
            }
        }

        private void BtnClose_OnClick(object sender, RoutedEventArgs e) => Close();
    }

    public sealed class ChatBrowseRow : INotifyPropertyChanged
    {
        public TL.Message TgMessage { get; }
        public int MessageId => TgMessage.ID;
        public string DateText { get; }
        public string KindText { get; }
        public string PreviewText { get; }
        public bool CanDownload { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public ChatBrowseRow(TL.Message m, ChatDto chat)
        {
            TgMessage = m;
            DateText = m.date.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            KindText = DescribeKind(m);
            var textOneLine = (m.message ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            var fileLabel = TelegramApp.GetDownloadPreviewLabel(m);
            string preview;
            if (!string.IsNullOrEmpty(fileLabel))
                preview = string.IsNullOrEmpty(textOneLine) ? fileLabel : $"{fileLabel} — {textOneLine}";
            else
                preview = textOneLine;
            PreviewText = preview.Length > 200 ? preview[..200] + "…" : preview;
            CanDownload = TelegramApp.CanSelectMessageForManualBrowse(m, chat);
        }

        private static string DescribeKind(TL.Message m)
        {
            if (m.media == null)
                return string.IsNullOrEmpty(m.message)
                    ? "—"
                    : (m.message.Contains("http", StringComparison.OrdinalIgnoreCase) ? "Link" : "Text");
            if (m.media is MessageMediaPhoto) return "Photo";
            if (m.media is MessageMediaDocument { document: Document d })
                return DocumentMediaKindHelper.GetMessageType(d).ToString();
            return m.media.GetType().Name.Replace("MessageMedia", "");
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
