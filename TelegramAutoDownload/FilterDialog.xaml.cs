using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MahApps.Metro.Controls;
using TelegramClient;
using TelegramClient.Models;

namespace TelegramAutoDownload
{
    // ── PatternEntry view-model ──────────────────────────────────────────────────

    public class PatternEntry : INotifyPropertyChanged
    {
        private string _pattern = string.Empty;

        public string Pattern
        {
            get => _pattern;
            set
            {
                _pattern = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(ValidationTooltip));
            }
        }

        public bool IsValid
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Pattern)) return false;
                try { _ = new Regex(Pattern, RegexOptions.None, TimeSpan.FromSeconds(1)); return true; }
                catch { return false; }
            }
        }

        // ValidColour kept for possible future use; border colour is now handled by DataTrigger in XAML
        public string ValidationTooltip => IsValid ? "Valid regex" : "Invalid regex — fix before saving";

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    // ── HistoryItem view-model ───────────────────────────────────────────────────

    public class HistoryMessageItem
    {
        public string Header  { get; init; } = string.Empty;
        public string Preview { get; init; } = string.Empty;
        public string RawText { get; init; } = string.Empty;
    }

    // ── FilterDialog ─────────────────────────────────────────────────────────────

    public partial class FilterDialog : MetroWindow
    {
        private readonly string _chatName;
        private readonly string _chatType;
        private readonly string _basePath;
        private readonly Func<Task<List<HistoryEntry>>>? _fetchMessages;

        private readonly ObservableCollection<PatternEntry> _patterns = [];

        /// <summary>Final pattern list — semicolon-joined — after user clicks Save.</summary>
        public List<string> ResultPatterns { get; private set; } = [];

        // ── Quick patterns defined once ──────────────────────────────────────────

        private static readonly (string Label, string Pattern, string Tooltip)[] QuickPatterns =
        [
            ("720p",       @"(?i).*720p.*",                     "Contains '720p'"),
            ("1080p",      @"(?i).*1080p.*",                    "Contains '1080p'"),
            ("4K / 2160p", @"(?i).*(4k|2160p).*",              "4K or 2160p"),
            ("HDR",        @"(?i).*hdr.*",                      "Contains 'HDR'"),
            ("Sample",     @"(?i).*sample.*",                   "Contains 'sample'"),
            ("Promo / Ad", @"(?i).*(promo|advertisement|\bad\b|sponsor)", "Promotional / ad content"),
            (".exe",       @"(?i).*\.exe$",                     "Executable files"),
            (".zip",       @"(?i).*\.zip$",                     "ZIP archives"),
            (".rar / .7z", @"(?i).*\.(rar|7z)$",               "Compressed archives"),
            ("URL",        @"https?://\S+",                     "HTTP/HTTPS links"),
            ("Subscribe",  @"(?i).*(subscribe|follow|like).*",  "Subscribe / follow messages"),
            ("Join",       @"(?i).*(join|invite|channel).*",    "Join / invite messages"),
        ];

        // ────────────────────────────────────────────────────────────────────────

        public FilterDialog(
            IEnumerable<string> currentPatterns,
            string chatName,
            string chatType,
            string basePath,
            Func<Task<List<HistoryEntry>>>? fetchMessages = null)
        {
            InitializeComponent();

            _chatName      = chatName;
            _chatType      = chatType;
            _basePath      = basePath;
            _fetchMessages = fetchMessages;

            foreach (var p in currentPatterns)
                _patterns.Add(new PatternEntry { Pattern = p });

            PatternList.ItemsSource = _patterns;

            BuildQuickPatternChips();
            _ = LoadHistoryAsync();
        }

        // ── Quick pattern chips ──────────────────────────────────────────────────

        private void BuildQuickPatternChips()
        {
            foreach (var (label, pattern, tooltip) in QuickPatterns)
            {
                var btn = new Button
                {
                    Content = label,
                    Tag     = pattern,
                    ToolTip = $"{tooltip}\n→ {pattern}",
                    Style   = (Style)Resources["ChipBtn"],
                };
                btn.Click += ChipButton_Click;
                QuickPatternPanel.Children.Add(btn);
            }
        }

        private void ChipButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string p)
                AddPatternEntry(p);
        }

        // ── Pattern list CRUD ────────────────────────────────────────────────────

        private void BtnAddPattern_Click(object sender, RoutedEventArgs e)
            => AddPatternEntry(string.Empty);

        private void AddPatternEntry(string pattern)
        {
            _patterns.Add(new PatternEntry { Pattern = pattern });
            // Scroll to bottom so the new entry is visible
            if (PatternList.ItemContainerGenerator.ContainerFromIndex(_patterns.Count - 1)
                is FrameworkElement el)
                el.BringIntoView();
        }

        private void BtnDeletePattern_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PatternEntry entry)
                _patterns.Remove(entry);

            UpdateTestResults();
        }

        // ── Live test ────────────────────────────────────────────────────────────

        private void TxtTest_TextChanged(object sender, TextChangedEventArgs e)
            => UpdateTestResults();

        private void TestMode_Changed(object sender, RoutedEventArgs e)
            => UpdateTestResults();

        private void UpdateTestResults()
        {
            // Guard against being called before InitializeComponent completes
            if (TxtTest == null || TestResults == null || TxtTestHint == null) return;

            var input = TxtTest.Text ?? string.Empty;
            var results = new List<string>();

            if (string.IsNullOrEmpty(input))
            {
                TxtTestHint.Visibility = Visibility.Visible;
                TestResults.ItemsSource = null;
                return;
            }

            TxtTestHint.Visibility = Visibility.Collapsed;
            bool isFileMode = RbFile.IsChecked == true;
            string modeLabel = isFileMode ? "file" : "message";

            var validPatterns = _patterns.Where(p => p.IsValid).ToList();
            if (validPatterns.Count == 0)
            {
                results.Add("ℹ  No valid patterns to test.");
            }
            else
            {
                foreach (var entry in validPatterns)
                {
                    bool match = false;
                    try
                    {
                        match = Regex.IsMatch(input, entry.Pattern,
                            RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                    }
                    catch { /* malformed — just treat as no-match */ }

                    string action = isFileMode ? "→ skip file" : "→ capture as .txt";
                    results.Add(match
                        ? $"✓  \"{entry.Pattern}\"  matches {modeLabel}  {action}"
                        : $"✗  \"{entry.Pattern}\"  no match");
                }
            }

            TestResults.ItemsSource = results;
        }

        // ── Load history messages ────────────────────────────────────────────────

        private async System.Threading.Tasks.Task LoadHistoryAsync()
        {
            TxtHistoryStatus.Text = "Loading…";

            try
            {
                List<HistoryEntry> entries;

                if (_fetchMessages != null)
                {
                    // Fetch live from Telegram
                    entries = await _fetchMessages();
                    if (entries.Count == 0)
                    {
                        TxtHistoryStatus.Text = "(not connected or no messages)";
                        return;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(_basePath))
                {
                    // Fallback: read from local JSONL history file
                    entries = await ChatHistoryService.ReadHistoryAsync(_chatType, _chatName, _basePath);
                    if (entries.Count == 0)
                    {
                        TxtHistoryStatus.Text = "(no history file yet)";
                        return;
                    }
                    entries = [.. entries.TakeLast(50).Reverse()];
                }
                else
                {
                    TxtHistoryStatus.Text = "(not connected)";
                    return;
                }

                var items = entries
                    .Where(e => !string.IsNullOrWhiteSpace(e.Text) || !string.IsNullOrWhiteSpace(e.FileName))
                    .Select(e => new HistoryMessageItem
                    {
                        Header  = $"{e.Date:dd MMM yyyy HH:mm}  {e.SenderName ?? ""}  {(e.MediaType != null ? $"[{e.MediaType}]" : "")}".Trim(),
                        Preview = !string.IsNullOrWhiteSpace(e.Text) ? e.Text : e.FileName ?? string.Empty,
                        RawText = !string.IsNullOrWhiteSpace(e.Text) ? e.Text : e.FileName ?? string.Empty,
                    })
                    .ToList();

                TxtHistoryStatus.Text = $"({entries.Count} messages)";
                HistoryList.ItemsSource = items;
            }
            catch (Exception ex)
            {
                TxtHistoryStatus.Text = $"(error: {ex.Message})";
            }
        }

        private void BtnAddFromHistory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not HistoryMessageItem item) return;

            // Use the raw text as a literal pattern (escape special regex chars)
            var escaped = Regex.Escape(item.RawText);
            AddPatternEntry(escaped);
        }

        // ── Save / Cancel ────────────────────────────────────────────────────────

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // Warn if any pattern is invalid
            var invalid = _patterns.Where(p => !string.IsNullOrWhiteSpace(p.Pattern) && !p.IsValid).ToList();
            if (invalid.Count > 0)
            {
                var list = string.Join("\n  ", invalid.Select(p => p.Pattern));
                var res  = MessageBox.Show(
                    $"The following patterns are invalid regex and will be removed:\n\n  {list}\n\nSave anyway?",
                    "Invalid patterns", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res != MessageBoxResult.Yes) return;
            }

            ResultPatterns = _patterns
                .Where(p => !string.IsNullOrWhiteSpace(p.Pattern) && p.IsValid)
                .Select(p => p.Pattern)
                .ToList();

            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;
    }
}
