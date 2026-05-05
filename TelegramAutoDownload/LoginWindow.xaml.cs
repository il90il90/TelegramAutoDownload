using MahApps.Metro.Controls;
using QRCoder;
using System;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TelegramAutoDownload.Models;
using TelegramClient;
using TL;

namespace TelegramAutoDownload
{
    public partial class LoginWindow : MetroWindow
    {
        private readonly TelegramApp _telegram;
        private readonly ConfigFile _configFile;
        private DispatcherTimer _qrTimer;
        private int _qrCountdown;

        public LoginWindow()
        {
            try
            {
                InitializeComponent();
                _configFile = new ConfigFile();
                var configParams = _configFile.Read();

                if (configParams.AppId == 0 || string.IsNullOrEmpty(configParams.ApiHash))
                    throw new InvalidOperationException(
                        "APP_ID and API_HASH are not set. Please add them to your .env file.");

                _telegram = new TelegramApp(configParams.AppId, configParams.ApiHash);
                MoveToMainWindowIfConnected();

                // Initialize QR tab when tab is switched
                tabControl.SelectionChanged += TabControl_SelectionChanged;
            }
            catch (Exception e)
            {
                ShowError(e.Message);
            }
        }

        private void ShowError(string message)
        {
            if (txtError != null)
            {
                txtError.Text = message;
                txtError.Visibility = Visibility.Visible;
            }
            else
            {
                MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MoveToMainWindowIfConnected()
        {
            if (_telegram?.Client.UserId == 0) return;

            var mainWindow = new MainWindow(_telegram, _configFile);
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

                var phoneNumber = txtPhoneNumber.Text;
                var prefixPhoneNumber = !phoneNumber.StartsWith("+") ? $"+{phoneNumber}" : phoneNumber;
                await _telegram.Client.Login(prefixPhoneNumber);

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

                await _telegram.Client.Login(txtCode.Text);
                MoveToMainWindowIfConnected();
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

                await _telegram.Client.Login(txtPassword.Password);
                MoveToMainWindowIfConnected();
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

        private void TabControl_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (tabControl.SelectedIndex == 1)
            {
                // QR tab selected - start QR login flow
                _ = StartQrLoginAsync();
            }
        }

        private void BtnRefreshQr_Click(object sender, RoutedEventArgs e)
        {
            _ = StartQrLoginAsync();
        }

        private async System.Threading.Tasks.Task StartQrLoginAsync()
        {
            try
            {
                _qrTimer?.Stop();
                txtQrStatus.Text = "Generating QR code...";
                imgQrCode.Source = null;

                var exportedToken = await _telegram.Client.Auth_ExportLoginToken(
                    _telegram.Client.TLConfig.api_id,
                    _telegram.Client.TLConfig.api_hash,
                    Array.Empty<long>());

                if (exportedToken is Auth_LoginToken loginToken)
                {
                    var tokenBytes = loginToken.token;
                    var base64Token = Convert.ToBase64String(tokenBytes);
                    var qrUrl = $"tg://login?token={Uri.EscapeDataString(base64Token)}";

                    DisplayQrCode(qrUrl);
                    StartQrCountdown(loginToken.expires);

                    // Listen for login token updates
                    _telegram.Client.OnUpdates += async updates =>
                    {
                        foreach (var update in updates.UpdateList)
                        {
                            if (update is UpdateLoginToken)
                            {
                                await Dispatcher.InvokeAsync(async () =>
                                {
                                    try
                                    {
                                        var result = await _telegram.Client.Auth_ExportLoginToken(
                                            _telegram.Client.TLConfig.api_id,
                                            _telegram.Client.TLConfig.api_hash,
                                            Array.Empty<long>());

                                        if (result is Auth_LoginTokenSuccess)
                                        {
                                            _qrTimer?.Stop();
                                            MoveToMainWindowIfConnected();
                                        }
                                        else if (result is Auth_LoginToken newToken)
                                        {
                                            var newBase64 = Convert.ToBase64String(newToken.token);
                                            var newUrl = $"tg://login?token={Uri.EscapeDataString(newBase64)}";
                                            DisplayQrCode(newUrl);
                                            StartQrCountdown(newToken.expires);
                                        }
                                    }
                                    catch { }
                                });
                            }
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                txtQrStatus.Text = $"Error: {ex.Message}";
            }
        }

        private void DisplayQrCode(string url)
        {
            try
            {
                var qrGenerator = new QRCodeGenerator();
                var qrData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
                var qrCode = new XamlQRCode(qrData);
                var qrImage = qrCode.GetGraphic(20);
                imgQrCode.Source = qrImage;
                txtQrStatus.Text = "Scan with Telegram to login";
            }
            catch (Exception ex)
            {
                txtQrStatus.Text = $"Failed to generate QR: {ex.Message}";
            }
        }

        private void StartQrCountdown(int expiresUnixTime)
        {
            _qrTimer?.Stop();
            _qrCountdown = 25;
            _qrTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _qrTimer.Tick += (s, e) =>
            {
                _qrCountdown--;
                txtQrCountdown.Text = $"Expires in {_qrCountdown}s";
                if (_qrCountdown <= 0)
                {
                    _qrTimer.Stop();
                    txtQrStatus.Text = "QR code expired. Click Refresh.";
                    txtQrCountdown.Text = string.Empty;
                }
            };
            _qrTimer.Start();
        }
    }
}
