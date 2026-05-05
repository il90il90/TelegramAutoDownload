
namespace BasePlugins
{
    public abstract class BasePlugin<TMessage> : IBasePlugin
    {
        public abstract string PluginName { get; }
        public virtual int Priority => 50;

        /// <summary>
        /// Called periodically during download: (chatName, fileName, pluginName, percent, bytesDownloaded, totalBytes)
        /// Set by the host before calling ExecuteAsync.
        /// </summary>
        public Action<string, string, string, double, long, long>? OnProgress { get; set; }

        /// <summary>
        /// Called when download finishes: (chatName, fileName, success)
        /// Set by the host before calling ExecuteAsync.
        /// </summary>
        public Action<string, string, bool>? OnComplete { get; set; }

        public abstract bool CanHandle(Config config);
        public abstract Task<ResultExecute> ExecuteAsync(Config config);

        protected void CreateDirectoryIfNotExist(string path)
        {
            var fullPath = $"";
            foreach (var item in path.Split("/"))
            {
                fullPath += @$"{item}/";
                if (!Directory.Exists(fullPath))
                    Directory.CreateDirectory(fullPath);
            }
        }
    }
}
