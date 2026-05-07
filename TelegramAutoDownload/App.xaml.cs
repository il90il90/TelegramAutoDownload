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
            // Initialize the logger FIRST so exception handlers can write to it immediately.
            Log.Logger = new LoggerConfiguration()
                .WriteTo.File(System.IO.Path.Combine(AppPaths.LogsDir, "app-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            // --- Global exception handlers: log and show instead of crashing ---
            DispatcherUnhandledException += (_, ex) =>
            {
                Log.Error(ex.Exception, "Unhandled UI exception");
                ShowCrashDialog(ex.Exception);
                ex.Handled = true; // Prevent silent crash
            };

            AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            {
                if (ex.ExceptionObject is Exception e2)
                    Log.Fatal(e2, "Unhandled background exception (isTerminating={t})", ex.IsTerminating);
            };

            TaskScheduler.UnobservedTaskException += (_, ex) =>
            {
                Log.Error(ex.Exception, "Unobserved Task exception");
                ex.SetObserved(); // Prevent process termination
            };

            // Load .env from writable AppData location (fallback to app directory for dev)
            var envPath = AppPaths.EnvFile;
            if (!System.IO.File.Exists(envPath))
                envPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
            if (System.IO.File.Exists(envPath))
                DotEnv.Load(options: new dotenv.net.DotEnvOptions(envFilePaths: new[] { envPath }));

            base.OnStartup(e);

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

            // If a session file already exists the user is (very likely) already logged in.
            // Show a clean splash/loading screen instead of the login form.
            var sessionPath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "TelegramAutoDownload", "session.dat");

            if (System.IO.File.Exists(sessionPath) && configFile.Read().AppId != 0)
                new SplashWindow(configFile).Show();
            else
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
            // Flush the download index before exit so no completed-download records are lost.
            // The index uses a debounced background save; this ensures pending writes are persisted.
            TelegramClient.FileDownloadIndex.Flush();
            TrayIcon?.Dispose();
            Log.Information("Application shutting down");
            Log.CloseAndFlush();
            base.OnExit(e);
        }

        private static void ShowCrashDialog(Exception ex)
        {
            try
            {
                var logPath = System.IO.Path.Combine(AppPaths.LogsDir, "app-.log");
                MessageBox.Show(
                    $"An unexpected error occurred:\n\n{ex.Message}\n\nDetails have been saved to the log file:\n{logPath}",
                    "TelegramAutoDownload — Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { /* ShowCrashDialog itself must never throw */ }
        }
    }
}
