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
        private TelegramApp? _telegram;
        private readonly ConfigFile _configFile;
        private ConfigParams _configParams = null!;
        private DispatcherTimer? _qrTimer;
        private int _qrCountdown;

        public LoginWindow(ConfigFile configFile)
        {
            InitializeComponent();
            _configFile = configFile;
            _configParams = configFile.Read();
            tabControl.SelectionChanged += TabControl_SelectionChanged;

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
                else return; // User cancelled settings, stay on login screen
            }

            try
            {
                loadingRing.IsActive = true;
                txtQrStatus.Text = "Connecting to Telegram...";

                // Create TelegramApp off the UI thread to avoid capturing the WPF
                // SynchronizationContext inside WTelegram (prevents deadlocks)
                _telegram = await System.Threading.Tasks.Task.Run(() =>
                    new TelegramApp(_configParams.AppId, _configParams.ApiHash));

                // Allow WTelegram to establish the TCP connection and validate the session
                await System.Threading.Tasks.Task.Delay(2000);

                if (_telegram.Client.UserId != 0)
                {
                    // Already authenticated — go directly to MainWindow
                    MoveToMainWindow();
                    return;
                }

                // Auto-start QR if that tab is already selected
                if (tabControl.SelectedIndex == 1)
                    _ = StartQrLoginAsync();
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

                // Ensure TelegramApp exists (may not if credentials were just entered)
                _telegram ??= await System.Threading.Tasks.Task.Run(() =>
                    new TelegramApp(_configParams.AppId, _configParams.ApiHash));

                var phoneNumber = txtPhoneNumber.Text;
                var prefixPhoneNumber = !phoneNumber.StartsWith("+") ? $"+{phoneNumber}" : phoneNumber;
                await _telegram!.Client.Login(prefixPhoneNumber);

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

        private void TabControl_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (tabControl.SelectedIndex != 1) return;

            if (_telegram == null)
            {
                txtQrStatus.Text = "Connecting... please wait, then click Refresh.";
                return;
            }
            _ = StartQrLoginAsync();
        }

        private void BtnRefreshQr_Click(object sender, RoutedEventArgs e)
        {
            if (_telegram == null)
            {
                txtQrStatus.Text = "Still connecting... please wait.";
                return;
            }
            _ = StartQrLoginAsync();
        }

        private async System.Threading.Tasks.Task StartQrLoginAsync()
        {
            try
            {
                _qrTimer?.Stop();
                txtQrStatus.Text = "Generating QR code...";
                imgQrCode.Source = null;

                var exportedToken = await _telegram!.Client.Auth_ExportLoginToken(
                    _configParams.AppId,
                    _configParams.ApiHash,
                    Array.Empty<long>());

                if (exportedToken is Auth_LoginToken loginToken)
                {
                    var base64Token = Convert.ToBase64String(loginToken.token)
                        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

                    DisplayQrCode($"tg://login?token={base64Token}");
                    StartQrCountdown(loginToken.expires);

                    _telegram.Client.OnUpdates += async updates =>
                    {
                        foreach (var update in updates.UpdateList)
                        {
                            if (update is not UpdateLoginToken) continue;

                            await Dispatcher.InvokeAsync(async () =>
                            {
                                try
                                {
                                    var result = await _telegram.Client.Auth_ExportLoginToken(
                                        _configParams.AppId, _configParams.ApiHash, Array.Empty<long>());

                                    if (result is Auth_LoginTokenSuccess)
                                    {
                                        _qrTimer?.Stop();
                                        MoveToMainWindow();
                                    }
                                    else if (result is Auth_LoginToken newToken)
                                    {
                                        var newBase64 = Convert.ToBase64String(newToken.token)
                                            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
                                        DisplayQrCode($"tg://login?token={newBase64}");
                                        StartQrCountdown(newToken.expires);
                                    }
                                }
                                catch { }
                            });
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
                var pngQr = new PngByteQRCode(qrData);
                var pngBytes = pngQr.GetGraphic(10);

                using var ms = new System.IO.MemoryStream(pngBytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = ms;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                imgQrCode.Source = bitmap;

                txtQrStatus.Text = "Scan with Telegram to login";
            }
            catch (Exception ex)
            {
                txtQrStatus.Text = $"Failed to generate QR: {ex.Message}";
            }
        }

        private void StartQrCountdown(DateTime expires)
        {
            _qrTimer?.Stop();
            _qrCountdown = Math.Max(1, (int)(expires - DateTime.UtcNow).TotalSeconds);
            _qrTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _qrTimer.Tick += (s, e) =>
            {
                _qrCountdown--;
                txtQrCountdown.Text = $"Expires in {_qrCountdown}s";
                if (_qrCountdown <= 0)
                {
                    _qrTimer!.Stop();
                    txtQrStatus.Text = "QR code expired. Click Refresh.";
                    txtQrCountdown.Text = string.Empty;
                }
            };
            _qrTimer.Start();
        }
    }
}
