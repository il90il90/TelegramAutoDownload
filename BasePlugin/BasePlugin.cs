
namespace BasePlugins
{
    public abstract class BasePlugin<TMessage> : IBasePlugin
    {
        public abstract string PluginName { get; }

        public abstract bool CanHandle(Config config);
        public abstract Task<bool> ExecuteAsync(Config config);

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
