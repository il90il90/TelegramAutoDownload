using MahApps.Metro.Controls;
using System;
using System.Windows;
using TelegramAutoDownload.Models;
using TelegramClient;

namespace TelegramAutoDownload
{
    public partial class LoginWindow : MetroWindow
    {
        private TelegramApp? _telegram;
        private readonly ConfigFile _configFile;
        private ConfigParams _configParams = null!;

        public LoginWindow(ConfigFile configFile)
        {
            InitializeComponent();
            _configFile = configFile;
            _configParams = configFile.Read();

            // Start async init after window is fully rendered
            Loaded += async (_, __) => await InitAsync();
        }

        private async System.Threading.Tasks.Task InitAsync()
        {
            // Missing credentials — open Settings immediately
            if (_configParams.AppId == 0 || string.IsNullOrEmpty(_configParams.ApiHash))
            {
                ShowError("APP_ID and API_HASH are not configured. Opening Settings...");
                await System.Threading.Tasks.Task.Delay(600);
                var settings = new SettingsWindow(_configFile) { Owner = this };
                if (settings.ShowDialog() == true)
                {
                    txtError.Visibility = Visibility.Collapsed;
                    _configParams = _configFile.Read();
                }
                else return;
            }

            try
            {
                loadingRing.IsActive = true;

                _telegram = await System.Threading.Tasks.Task.Run(() =>
                    new TelegramApp(_configParams.AppId, _configParams.ApiHash));

                // Give WTelegramClient a moment to restore session if somehow reached
                await System.Threading.Tasks.Task.Delay(800);

                if (_telegram.Client.UserId != 0)
                {
                    MoveToMainWindow();
                    return;
                }
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
            finally
            {
                loadingRing.IsActive = false;
            }
        }

        private void ShowError(string message)
        {
            txtError.Text = message;
            txtError.Visibility = Visibility.Visible;
        }

        private void MoveToMainWindow()
        {
            if (_telegram?.Client.UserId == 0) return;
            var mainWindow = new MainWindow(_telegram!, _configFile);
            mainWindow.Show();
            Close();
        }

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                txtError.Visibility = Visibility.Collapsed;
                loadingRing.IsActive = true;
                btnLogin.IsEnabled = false;

                _telegram ??= await System.Threading.Tasks.Task.Run(() =>
                    new TelegramApp(_configParams.AppId, _configParams.ApiHash));

                var phone = txtPhoneNumber.Text.Trim();
                if (!phone.StartsWith("+")) phone = "+" + phone;
                await _telegram!.Client.Login(phone);

                stepCode.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                btnLogin.IsEnabled = true;
            }
            finally
            {
                loadingRing.IsActive = false;
            }
        }

        private async void BtnEnterCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                txtError.Visibility = Visibility.Collapsed;
                loadingRing.IsActive = true;
                btnEnterCode.IsEnabled = false;

                await _telegram!.Client.Login(txtCode.Text);
                if (_telegram.Client.UserId != 0)
                    MoveToMainWindow();
                else
                    stepPassword.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                btnEnterCode.IsEnabled = true;
            }
            finally
            {
                loadingRing.IsActive = false;
            }
        }

        private async void BtnEnterPassword_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                txtError.Visibility = Visibility.Collapsed;
                loadingRing.IsActive = true;
                btnEnterPassword.IsEnabled = false;

                await _telegram!.Client.Login(txtPassword.Password);
                MoveToMainWindow();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                btnEnterPassword.IsEnabled = true;
            }
            finally
            {
                loadingRing.IsActive = false;
            }
        }
    }
}
