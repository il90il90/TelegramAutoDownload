﻿namespace BasePlugins
{
    public interface IBasePlugin
    {
        public string PluginName { get; }
        public bool CanHandle(Config config);
        public Task<ResultExecute> ExecuteAsync(Config config);
    }
}
