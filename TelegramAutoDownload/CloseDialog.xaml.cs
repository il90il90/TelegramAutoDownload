using MahApps.Metro.Controls;
using System.Windows;

namespace TelegramAutoDownload
{
    public enum CloseAction { Cancel, MinimizeToTray, Exit }

    public partial class CloseDialog : MetroWindow
    {
        public CloseAction Result { get; private set; } = CloseAction.Cancel;

        public CloseDialog()
        {
            InitializeComponent();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Result = CloseAction.Cancel;
            Close();
        }

        private void BtnTray_Click(object sender, RoutedEventArgs e)
        {
            Result = CloseAction.MinimizeToTray;
            Close();
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            Result = CloseAction.Exit;
            Close();
        }
    }
}
