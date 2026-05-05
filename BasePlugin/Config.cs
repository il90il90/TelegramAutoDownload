using System.Collections.Generic;

namespace BasePlugins
{
    public class Config
    {
        public required string ChatName { get; set; }
        public required string Text { get; set; }
        public required string PathSaveFile { get; set; }
        // Keys are PluginName values; missing key means enabled by default
        public Dictionary<string, bool> EnabledPlugins { get; set; } = new();
    }
}
