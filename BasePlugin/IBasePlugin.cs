namespace BasePlugins
{
    public interface IBasePlugin
    {
        public string PluginName { get; }
        public int Priority { get; }
        public bool CanHandle(Config config);
        public Task<ResultExecute> ExecuteAsync(Config config);
    }
}
