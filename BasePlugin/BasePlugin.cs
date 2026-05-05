
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

        /// <summary>
        /// Executes a download action with automatic retry (exponential backoff).
        /// Does NOT retry on OperationCanceledException.
        /// </summary>
        protected static async Task<T> WithRetryAsync<T>(Func<Task<T>> action, int maxAttempts = 3)
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    return await action();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch when (++attempt < maxAttempts)
                {
                    int delayMs = attempt == 1 ? 2000 : 5000;
                    await Task.Delay(delayMs);
                }
            }
        }

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
