using Newtonsoft.Json;
using System.IO;

namespace TelegramAutoDownload.Models
{
    public class ConfigFile
    {
        private readonly string _fileName = AppPaths.ConfigFile;

        public ConfigFile()
        {
            if (!File.Exists(_fileName))
                File.WriteAllText(_fileName,
                    JsonConvert.SerializeObject(new ConfigParams(), Formatting.Indented));
        }

        public void Save(ConfigParams config)
        {
            File.WriteAllText(_fileName, JsonConvert.SerializeObject(config, Formatting.Indented));
        }

        public ConfigParams Read()
        {
            var data = File.ReadAllText(_fileName);
            return JsonConvert.DeserializeObject<ConfigParams>(data) ?? new ConfigParams();
        }
    }
}
