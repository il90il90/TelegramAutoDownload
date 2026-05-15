using MahApps.Metro.Controls;
using System;
using System.Threading.Tasks;
using System.Windows;
using TelegramAutoDownload.Models;
using TelegramClient;

namespace TelegramAutoDownload
{
    /// <summary>
    /// Shown on startup when a session file already exists.
    /// Connects silently and opens MainWindow on success, or falls back to LoginWindow on failure.
    /// </summary>
    public partial class SplashWindow : MetroWindow
    {
        private readonly ConfigFile _configFile;

        public SplashWindow(ConfigFile configFile)
        {
            InitializeComponent();
            _configFile = configFile;
            Loaded += async (_, __) => await ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            var config = _configFile.Read();

            try
            {
                SetStatus("Connecting…");

                var telegram = await Task.Run(() =>
                    new TelegramApp(config.AppId, config.ApiHash));

                await Task.Run(() => telegram.WaitForLoginAsync(60000));
                if (!await telegram.EnsureTelegramReadyAsync().ConfigureAwait(true))
                {
                    TelegramSessionHelper.DeleteSessionFile();
                    FallbackToLogin();
                    return;
                }

                if (telegram.Client.UserId != 0)
                {
                    SetStatus("Connected ✓");
                    await Task.Delay(300);

                    var main = new MainWindow(telegram, _configFile);
                    main.Show();
                    Close();
                    return;
                }

                // Session existed but is no longer valid — fall through to login
                FallbackToLogin();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "SplashWindow: connection failed, falling back to login");
                if (TelegramSessionHelper.IsAuthKeyError(ex))
                    TelegramSessionHelper.DeleteSessionFile();
                FallbackToLogin();
            }
        }

        private void FallbackToLogin()
        {
            var login = new LoginWindow(_configFile);
            login.Show();
            Close();
        }

        private void SetStatus(string text) =>
            Dispatcher.Invoke(() => TxtStatus.Text = text);
    }
}
