using BasePlugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TelegramClient.Factory.Base;
using TelegramClient.Factory.FactoriesMessages.Enum;
using TelegramClient.Models;
using TL;
using WTelegram;

namespace TelegramClient.Factory.Service
{
    public class MessageTextFactoryService : BaseMessage
    {
        private readonly List<Type> _pluginTypes = [];
        public override MessageTypes TypeMessage { get; }

        public MessageTextFactoryService(Client client, string pathFolderToSaveFiles) : base(client, pathFolderToSaveFiles)
        {
            // Use the absolute path next to the executable so the app works both
            // as a portable install (writable folder) and as a Program Files install
            // (read-only folder — Plugins were placed there by the installer).
            var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
            if (!Directory.Exists(pluginsDir))
            {
                // Only try to create the folder; silently skip if the install dir is read-only
                // (Program Files) — the installer is responsible for creating it there.
                try { Directory.CreateDirectory(pluginsDir); } catch { /* read-only install, skip */ }
            }

            var folders = Directory.Exists(pluginsDir)
                ? Directory.GetDirectories(pluginsDir)
                : Array.Empty<string>();

            foreach (var folder in folders)
            {
                var pluginFiles = Directory.GetFiles(folder, "*.dll");

                foreach (var pluginFile in pluginFiles)
                {
                    Assembly pluginAssembly = Assembly.LoadFrom(pluginFile);

                    _pluginTypes.AddRange(pluginAssembly.GetTypes()
                    .Where(t => !t.IsAbstract && t.IsClass &&
                                t.BaseType != null && t.BaseType.IsGenericType &&
                                t.BaseType.GetGenericTypeDefinition() == typeof(BasePlugin<>)));
                }
            }
        }

        public override async Task<ResultExecute> ExecuteAsync(Message message, ChatDto chatDto)
        {
            ResultExecute resultExecute = new(chatDto.Name);
            var split = (message.message ?? string.Empty).Split('\n');
            foreach (var line in split)
            {
                // Sort by priority on each line to ensure deterministic ordering
                var orderedPlugins = _pluginTypes
                    .Select(t => t.MakeGenericType(typeof(Message)))
                    .Select(t => Activator.CreateInstance(t) as BasePlugin<Message>)
                    .Where(p => p != null)
                    .OrderBy(p => p!.Priority)
                    .ToList();

                foreach (var pluginInstance in orderedPlugins)
                {
                    var config = new BasePlugins.Config
                    {
                        Text = line,
                        PathSaveFile = PathFolderToSaveFiles,
                        ChatName = chatDto.Name,
                        EnabledPlugins = chatDto.EnabledPlugins,
                    };

                    if (!pluginInstance!.CanHandle(config)) continue;

                    // Skip if plugin is explicitly disabled for this chat (missing key = enabled)
                    if (config.EnabledPlugins.TryGetValue(pluginInstance.PluginName, out var enabled) && !enabled)
                        continue;

                    // Wire progress callbacks so the plugin can report to the UI
                    pluginInstance.OnProgress = OnProgress;
                    pluginInstance.OnComplete = OnComplete;

                    resultExecute = await pluginInstance.ExecuteAsync(config);
                    if (resultExecute.IsSuccess) break;
                }

                if (resultExecute.IsSuccess) break;
            }
            return resultExecute;
        }
    }
}
