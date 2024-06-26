using BasePlugins;

namespace TorrentPlugin
{
    public class TorrentPlugin<TMessage> : BasePlugin<TMessage>
    {
        public override string PluginName => throw new NotImplementedException();

        public override bool CanHandle(Config config)
        {
            return config.Text.StartsWith("magnet:");
        }

        public override async Task<ResultExecute> ExecuteAsync(Config config)
        {
            Console.WriteLine("Download complete!");
            return new ResultExecute();
        }
    }
}
