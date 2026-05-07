using MahApps.Metro.Controls;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TelegramClient;
using TelegramClient.Models;

namespace TelegramAutoDownload
{
    public partial class MembersExportDialog : MetroWindow
    {
        private readonly TelegramApp          _telegram;
        private readonly ChatDto              _chat;
        private List<MemberEntry>             _members = [];
        private CancellationTokenSource?      _cts;

        public MembersExportDialog(TelegramApp telegram, ChatDto chat)
        {
            InitializeComponent();
            _telegram = telegram;
            _chat     = chat;
            TxtTitle.Text = $"Members — {chat.Name}";
        }

        private async void BtnFetch_Click(object sender, RoutedEventArgs e)
        {
            BtnFetch.IsEnabled  = false;
            BtnCsv.IsEnabled    = false;
            BtnVcard.IsEnabled  = false;
            DgMembers.ItemsSource = null;
            _members = [];
            PbProgress.Value = 0;
            TxtStatus.Text   = "Fetching members…";
            TxtCount.Text    = string.Empty;

            _cts = new CancellationTokenSource();

            try
            {
                _members = await _telegram.GetChannelMembersAsync(
                    _chat,
                    onProgress: (fetched, total) => Dispatcher.Invoke(() =>
                    {
                        TxtStatus.Text   = $"Fetched {fetched:N0} / {(total > 0 ? total.ToString("N0") : "?")} members…";
                        PbProgress.Value = total > 0 ? (double)fetched / total * 100 : 0;
                    }),
                    cancellationToken: _cts.Token);

                DgMembers.ItemsSource = _members;
                PbProgress.Value      = 100;

                var withPhone  = _members.Count(m => !string.IsNullOrEmpty(m.Phone));
                var withUser   = _members.Count(m => !string.IsNullOrEmpty(m.Username));
                TxtStatus.Text = "Done.";
                TxtCount.Text  = $"{_members.Count:N0} members  •  {withUser:N0} with @username  •  {withPhone:N0} with phone";

                BtnCsv.IsEnabled   = _members.Count > 0;
                BtnVcard.IsEnabled = _members.Count > 0;
            }
            catch (OperationCanceledException)
            {
                TxtStatus.Text = "Cancelled.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error fetching members", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtStatus.Text = "Error — see message above.";
            }
            finally
            {
                BtnFetch.IsEnabled = true;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void BtnCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title      = "Save members as CSV",
                Filter     = "CSV file|*.csv",
                FileName   = $"{SanitiseName(_chat.Name)}_members.csv",
                DefaultExt = "csv"
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("UserId,Username,FirstName,LastName,Phone,IsAdmin,IsBot");
                foreach (var m in _members)
                {
                    sb.AppendLine(
                        $"{m.UserId}," +
                        $"{CsvEscape(m.Username)}," +
                        $"{CsvEscape(m.FirstName)}," +
                        $"{CsvEscape(m.LastName)}," +
                        $"{CsvEscape(m.Phone)}," +
                        $"{m.IsAdmin}," +
                        $"{m.IsBot}");
                }
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show($"Saved {_members.Count:N0} rows to:\n{dlg.FileName}",
                    "CSV exported", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Export error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnVcard_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title      = "Save members as vCard",
                Filter     = "vCard file|*.vcf",
                FileName   = $"{SanitiseName(_chat.Name)}_members.vcf",
                DefaultExt = "vcf"
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                var sb = new StringBuilder();
                foreach (var m in _members)
                {
                    if (m.IsBot) continue; // skip bots — not useful as contacts
                    sb.AppendLine("BEGIN:VCARD");
                    sb.AppendLine("VERSION:3.0");
                    sb.AppendLine($"N:{VcardEscape(m.LastName)};{VcardEscape(m.FirstName)};;;");
                    sb.AppendLine($"FN:{VcardEscape(m.DisplayName)}");
                    if (!string.IsNullOrEmpty(m.Username))
                        sb.AppendLine($"NICKNAME:{VcardEscape(m.Username)}");
                    if (!string.IsNullOrEmpty(m.Phone))
                        sb.AppendLine($"TEL;TYPE=CELL:{m.Phone}");
                    sb.AppendLine($"X-TELEGRAM-ID:{m.UserId}");
                    sb.AppendLine("END:VCARD");
                }
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);

                var nonBots = _members.Count(m => !m.IsBot);
                MessageBox.Show($"Saved {nonBots:N0} contacts to:\n{dlg.FileName}",
                    "vCard exported", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Export error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            base.OnClosed(e);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static string CsvEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                return $"\"{s.Replace("\"", "\"\"")}\"";
            return s;
        }

        private static string VcardEscape(string s) =>
            s.Replace("\\", "\\\\").Replace(",", "\\,").Replace(";", "\\;").Replace("\n", "\\n");

        private static string SanitiseName(string name) =>
            string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }
}
