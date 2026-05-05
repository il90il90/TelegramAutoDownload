using ControlzEx.Theming;
using dotenv.net;
using Serilog;
using System;
using System.Drawing;
using System.Windows;
using WinForms = System.Windows.Forms;
using TelegramAutoDownload.Models;

namespace TelegramAutoDownload
{
    public partial class App : System.Windows.Application
    {
        public static WinForms.NotifyIcon? TrayIcon { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Load .env from writable AppData location (fallback to app directory for dev)
            var envPath = AppPaths.EnvFile;
            if (!System.IO.File.Exists(envPath))
                envPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
            if (System.IO.File.Exists(envPath))
                DotEnv.Load(options: new dotenv.net.DotEnvOptions(envFilePaths: new[] { envPath }));

            base.OnStartup(e);

            // Initialize file-based logger in writable AppData folder (rolling daily, keep 7 days)
            Log.Logger = new LoggerConfiguration()
                .WriteTo.File(System.IO.Path.Combine(AppPaths.LogsDir, "app-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("Application starting up");

            // Apply saved theme before any window opens
            var configFile = new ConfigFile();
            try
            {
                var config = configFile.Read();
                ThemeManager.Current.ChangeTheme(this, config.DarkMode ? "Dark.Blue" : "Light.Blue");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not apply saved theme");
            }

            // System tray icon
            var contextMenu = new WinForms.ContextMenuStrip();
            contextMenu.Items.Add("Show", null, (_, __) => ShowMainWindow());
            contextMenu.Items.Add(new WinForms.ToolStripSeparator());
            contextMenu.Items.Add("Exit", null, (_, __) => { TrayIcon?.Dispose(); Shutdown(); });

            TrayIcon = new WinForms.NotifyIcon
            {
                Text = "Telegram Auto Download",
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                    System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName),
                ContextMenuStrip = contextMenu,
                Visible = true
            };
            TrayIcon.DoubleClick += (_, __) => ShowMainWindow();

            new LoginWindow(configFile).Show();
        }

        private static void ShowMainWindow()
        {
            foreach (Window w in Current.Windows)
            {
                w.Show();
                w.WindowState = WindowState.Normal;
                w.Activate();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            TrayIcon?.Dispose();
            Log.Information("Application shutting down");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
