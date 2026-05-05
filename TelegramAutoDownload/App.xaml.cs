using System.Windows;
using dotenv.net;

namespace TelegramAutoDownload
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DotEnv.Load();
            base.OnStartup(e);
        }
    }
}
