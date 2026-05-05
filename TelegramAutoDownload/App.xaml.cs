using dotenv.net;
using System.Windows;
using TelegramAutoDownload.Models;

namespace TelegramAutoDownload
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DotEnv.Load();
            base.OnStartup(e);

            // Show LoginWindow immediately — never leave the user with a blank screen.
            // LoginWindow itself handles the "already connected → go to MainWindow" logic
            // asynchronously after it is displayed.
            var configFile = new ConfigFile();
            new LoginWindow(configFile).Show();
        }
    }
}
