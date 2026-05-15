using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.Controls;
using TelegramAutoDownload.Models;
using TelegramAutoDownload.Services;

namespace TelegramAutoDownload
{
    public partial class LogViewerWindow : MetroWindow
    {
        private const int MaxChars = 800_000;
        private LogPointer? _pendingPointer;
        private string _rawFileText = string.Empty;

        public LogViewerWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                RefreshFileList(selectFirst: _pendingPointer == null);
                if (_pendingPointer != null)
                    NavigateToPointer(_pendingPointer);
            };
        }

        /// <summary>Opens the log viewer and jumps to the given entry when possible.</summary>
        public static void Open(LogPointer? pointer, Window? owner = null)
        {
            var w = new LogViewerWindow { _pendingPointer = pointer };
            if (owner != null)
                w.Owner = owner;
            w.Show();
            w.Activate();
        }

        private void RefreshFileList(bool selectFirst)
        {
            lstLogs.Items.Clear();
            tbContent.Text = string.Empty;
            _rawFileText = string.Empty;
            if (!Directory.Exists(AppPaths.LogsDir))
            {
                Directory.CreateDirectory(AppPaths.LogsDir);
                return;
            }

            var files = Directory.GetFiles(AppPaths.LogsDir, "app-*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
            foreach (var path in files)
                lstLogs.Items.Add(path);

            if (_pendingPointer?.FilePath != null && File.Exists(_pendingPointer.FilePath))
            {
                for (var i = 0; i < lstLogs.Items.Count; i++)
                {
                    if ((string)lstLogs.Items[i] == _pendingPointer.FilePath)
                    {
                        lstLogs.SelectedIndex = i;
                        return;
                    }
                }
            }

            if (selectFirst && lstLogs.Items.Count > 0)
                lstLogs.SelectedIndex = 0;
        }

        private void LstLogs_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstLogs.SelectedItem is not string path || !File.Exists(path))
            {
                tbContent.Text = string.Empty;
                _rawFileText = string.Empty;
                return;
            }

            try
            {
                _rawFileText = ReadLogTail(path, MaxChars);
                ApplyFilterAndDisplay();
                if (_pendingPointer != null && string.Equals(_pendingPointer.FilePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    NavigateToPointer(_pendingPointer);
                    _pendingPointer = null;
                }
            }
            catch (Exception ex)
            {
                tbContent.Text = ex.ToString();
            }
        }

        private void ApplyFilterAndDisplay()
        {
            if (string.IsNullOrEmpty(_rawFileText))
            {
                tbContent.Text = string.Empty;
                return;
            }

            var showInf = chkShowInf.IsChecked == true;
            var showWrn = chkShowWrn.IsChecked == true;
            var showErr = chkShowErr.IsChecked == true;

            var sb = new StringBuilder();
            var lineNo = 0;
            foreach (var line in _rawFileText.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (trimmed.Length == 0)
                {
                    sb.AppendLine();
                    continue;
                }

                var level = DetectLevel(trimmed);
                if (level == "WRN" && !showWrn) continue;
                if (level is "ERR" or "FTL" && !showErr) continue;
                if (level == "INF" && !showInf) continue;
                if (level == "DBG" && !showInf) continue;

                lineNo++;
                sb.AppendLine($"{lineNo,5} | {trimmed}");
            }

            tbContent.Text = sb.ToString();
        }

        private static string DetectLevel(string line)
        {
            if (line.Contains("[ERR]", StringComparison.Ordinal) || line.Contains("[FTL]", StringComparison.Ordinal))
                return "ERR";
            if (line.Contains("[WRN]", StringComparison.Ordinal))
                return "WRN";
            if (line.Contains("[INF]", StringComparison.Ordinal))
                return "INF";
            if (line.Contains("[DBG]", StringComparison.Ordinal))
                return "DBG";
            return "";
        }

        private void NavigateToPointer(LogPointer pointer)
        {
            chkShowWrn.IsChecked = true;
            chkShowErr.IsChecked = true;
            ApplyFilterAndDisplay();

            if (string.IsNullOrWhiteSpace(pointer.SearchText))
            {
                tbNavStatus.Text = "No search anchor for this entry.";
                return;
            }

            var haystack = tbContent.Text;
            if (haystack.Length == 0)
            {
                tbNavStatus.Text = "Log file is empty or still loading.";
                return;
            }

            var idx = haystack.LastIndexOf(pointer.SearchText, StringComparison.Ordinal);
            if (idx < 0)
            {
                // Try shorter anchor (first 40 chars)
                var shortAnchor = pointer.SearchText.Length > 40
                    ? pointer.SearchText[..40]
                    : pointer.SearchText;
                idx = haystack.LastIndexOf(shortAnchor, StringComparison.Ordinal);
            }

            if (idx < 0)
            {
                tbNavStatus.Text = $"Could not find this entry in the visible log. Try Refresh or open file: {Path.GetFileName(pointer.FilePath)}";
                return;
            }

            tbContent.Focus();
            tbContent.SelectionStart = idx;
            tbContent.SelectionLength = Math.Min(pointer.SearchText.Length, haystack.Length - idx);
            var lineIndex = haystack[..idx].Count(c => c == '\n');
            tbContent.ScrollToLine(lineIndex);
            tbContent.CaretIndex = idx + tbContent.SelectionLength;

            tbNavStatus.Text = $"[{pointer.Level}] {pointer.Summary}";
        }

        private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilterAndDisplay();

        private void BtnFindNextError_OnClick(object sender, RoutedEventArgs e)
        {
            var text = tbContent.Text;
            var start = tbContent.SelectionStart + Math.Max(1, tbContent.SelectionLength);
            var markers = new[] { "[FTL]", "[ERR]", "[WRN]" };
            var best = -1;
            foreach (var m in markers)
            {
                var i = text.IndexOf(m, start, StringComparison.Ordinal);
                if (i >= 0 && (best < 0 || i < best))
                    best = i;
            }

            if (best < 0)
            {
                tbNavStatus.Text = "No further warnings/errors in this view.";
                return;
            }

            tbContent.SelectionStart = best;
            tbContent.SelectionLength = 20;
            var line = text[..best].Count(c => c == '\n');
            tbContent.ScrollToLine(line);
            tbNavStatus.Text = "Next issue highlighted.";
        }

        private static string ReadLogTail(string path, int maxChars)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var len = fs.Length;
            if (len == 0) return string.Empty;

            var take = (int)Math.Min(len, maxChars);
            fs.Seek(-take, SeekOrigin.End);
            var buffer = new byte[take];
            _ = fs.Read(buffer, 0, take);
            var text = Encoding.UTF8.GetString(buffer);
            if (take < len)
                return "… (showing end of file only — use Open folder for full file)\r\n\r\n" + text;
            return text;
        }

        private void BtnRefresh_OnClick(object sender, RoutedEventArgs e)
        {
            var prev = lstLogs.SelectedItem as string;
            RefreshFileList(selectFirst: false);
            if (prev != null)
            {
                for (var i = 0; i < lstLogs.Items.Count; i++)
                {
                    if ((string)lstLogs.Items[i] == prev)
                    {
                        lstLogs.SelectedIndex = i;
                        break;
                    }
                }
            }
            else if (lstLogs.Items.Count > 0)
                lstLogs.SelectedIndex = 0;
        }

        private void BtnOpenFolder_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.LogsDir);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = AppPaths.LogsDir,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Open folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnDelete_OnClick(object sender, RoutedEventArgs e)
        {
            if (lstLogs.SelectedItem is not string path || !File.Exists(path))
            {
                MessageBox.Show(this, "Select a log file first.", "Delete", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(this,
                    $"Delete this file?\n{Path.GetFileName(path)}",
                    "Delete log",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                File.Delete(path);
                RefreshFileList(selectFirst: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Delete failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_OnClick(object sender, RoutedEventArgs e) => Close();
    }
}
