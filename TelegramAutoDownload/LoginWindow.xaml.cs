using System.Windows;
using TelegramAutoDownload.Models;
using TelegramClient;

namespace TelegramAutoDownload
{
    /// <summary>
    /// Interaction logic for LoginWindow.xaml
    /// </summary>
    public partial class LoginWindow : Window
    {
        readonly TelegramApp telegram;
        private ConfigFile _configFile;
        public LoginWindow()
        {
            try
            {
                InitializeComponent();
                _configFile = new ConfigFile();
                var _configParams = _configFile.Read();

                telegram = new TelegramApp(_configParams.AppId, _configParams.ApiHash);
                MoveToMainWindowIfConnected();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MoveToMainWindowIfConnected()
        {
            if (telegram.Client.UserId == 0) return;

            var mainWindow = new MainWindow(telegram, _configFile);
            mainWindow.Show();
            Close();
        }

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            var phoneNumber = txtPhoneNuber.Text;
            var prefixPhoneNumber = !phoneNumber.StartsWith("+") ? $"+{phoneNumber}" : phoneNumber;
            await telegram.Client.Login(prefixPhoneNumber);
            txtCode.Visibility = Visibility.Visible;
            tbCode.Visibility = Visibility.Visible;
            btnEnterCode.Visibility = Visibility.Visible;
            btnLogin.IsEnabled = false;
        }
        private async void BtnEnterCode_Click(object sender, RoutedEventArgs e)
        {
            txtPassword.Visibility = Visibility.Visible;
            tbPassword.Visibility = Visibility.Visible;
            btnEnterPassword.Visibility = Visibility.Visible;
            try
            {
                await telegram.Client.Login(txtCode.Text);
                MoveToMainWindowIfConnected();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void BtnEnterPassword_Click(object sender, RoutedEventArgs e)
        {

            try
            {
                await telegram.Client.Login(txtPassword.Password);
                MoveToMainWindowIfConnected();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
