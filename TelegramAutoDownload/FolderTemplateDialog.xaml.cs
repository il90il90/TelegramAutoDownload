using System;
using System.IO;
using System.Windows;
using MahApps.Metro.Controls;
using TelegramClient;

namespace TelegramAutoDownload
{
    public partial class FolderTemplateDialog : MetroWindow
    {
        private readonly string _basePath;
        private readonly string _chatName;
        private readonly string _chatType;
        private bool _suppressUpdate;

        /// <summary>Final template value chosen by the user — read this after DialogResult == true.</summary>
        public string ResultTemplate { get; private set; } = string.Empty;

        public FolderTemplateDialog(
            string currentTemplate,
            string chatName,
            string chatType,
            string basePath)
        {
            InitializeComponent();

            _basePath  = basePath;
            _chatName  = chatName;
            _chatType  = chatType;

            _suppressUpdate = true;
            TxtTemplate.Text = currentTemplate;
            _suppressUpdate = false;

            // If the current value is already an absolute path, show it in the browse label too
            if (Path.IsPathRooted(currentTemplate))
                TxtAbsolutePath.Text = currentTemplate;

            UpdatePreview();
        }

        // ── Token buttons ────────────────────────────────────────────────────

        private void Token_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            var token = btn.Tag as string ?? string.Empty;

            // When inserting a token, switch away from absolute-path mode
            if (Path.IsPathRooted(TxtTemplate.Text))
            {
                TxtTemplate.Text = string.Empty;
                TxtAbsolutePath.Text = "No folder selected — template above will be used";
            }

            var pos = TxtTemplate.CaretIndex;
            TxtTemplate.Text = TxtTemplate.Text.Insert(pos, token);
            TxtTemplate.CaretIndex = pos + token.Length;
            TxtTemplate.Focus();
        }

        // ── Template text box ────────────────────────────────────────────────

        private void TxtTemplate_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_suppressUpdate) return;

            // If user typed a rooted path by hand, reflect it in the browse label
            var text = TxtTemplate.Text?.Trim() ?? string.Empty;
            if (Path.IsPathRooted(text))
                TxtAbsolutePath.Text = text;
            else if (!string.IsNullOrEmpty(text))
                TxtAbsolutePath.Text = "No folder selected — template above will be used";

            UpdatePreview();
        }

        // ── Reset ────────────────────────────────────────────────────────────

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            TxtTemplate.Text = string.Empty;
            TxtAbsolutePath.Text = "No folder selected — template above will be used";
        }

        // ── Browse ───────────────────────────────────────────────────────────

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description       = "Select download folder for this chat",
                UseDescriptionForTitle = true,
                ShowNewFolderButton    = true,
            };

            // Open in the currently configured base path if available
            if (!string.IsNullOrWhiteSpace(_basePath) && Directory.Exists(_basePath))
                dialog.InitialDirectory = _basePath;

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var picked = dialog.SelectedPath;
                TxtAbsolutePath.Text = picked;

                _suppressUpdate = true;
                TxtTemplate.Text = picked;   // absolute path becomes the full template
                _suppressUpdate = false;

                UpdatePreview();
            }
        }

        // ── Preview ──────────────────────────────────────────────────────────

        private void UpdatePreview()
        {
            var template = TxtTemplate.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(template))
            {
                // Default layout: {Type}/{ChatName}
                TxtPreview.Text = Path.Combine(_basePath, _chatType, _chatName);
                TxtMode.Text    = "mode: default";
                return;
            }

            if (Path.IsPathRooted(template))
            {
                TxtPreview.Text = template;
                TxtMode.Text    = "mode: absolute path";
                return;
            }

            // Relative template — resolve tokens with sample date
            var sampleDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day);
            var resolved   = FolderTemplateHelper.Resolve(template, _chatType, _chatName, sampleDate)
                             ?? Path.Combine(_chatType, _chatName);
            TxtPreview.Text = Path.Combine(_basePath, resolved);
            TxtMode.Text    = "mode: template";
        }

        // ── OK / Cancel ──────────────────────────────────────────────────────

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            ResultTemplate = TxtTemplate.Text?.Trim() ?? string.Empty;
            DialogResult   = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
