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
            var PluginFolderName = "Plugins";
            if (!Directory.Exists(PluginFolderName))
                Directory.CreateDirectory(PluginFolderName);

            var folders = Directory.GetDirectories($"{AppDomain.CurrentDomain.BaseDirectory}/{PluginFolderName}");

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
            ResultExecute resultExecute = null;
            var split = message.message.Split('\n');
            foreach (var line in split)
            {
                foreach (var pluginType in _pluginTypes)
                {
                    var genericType = pluginType.MakeGenericType(typeof(Message));

                    if (Activator.CreateInstance(genericType) is BasePlugin<Message> pluginInstance)
                    {
                        var config = new BasePlugins.Config
                        {
                            Text = line,
                            PathSaveFile = PathFolderToSaveFiles,
                            ChatName = chatDto.Name,
                        };

                        if (pluginInstance.CanHandle(config))
                        {
                            resultExecute = await pluginInstance.ExecuteAsync(config);
                        }
                    }
                }
            }
            return resultExecute;
        }
    }
}
