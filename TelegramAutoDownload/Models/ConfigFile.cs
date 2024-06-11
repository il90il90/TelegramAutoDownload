using Newtonsoft.Json;
using System.IO;

namespace TelegramAutoDownload.Models
{
    public class ConfigFile
    {
        private readonly string _fileName = "config.txt";
        public ConfigFile()
        {
            var configParams = JsonConvert.SerializeObject(new ConfigParams(), Formatting.Indented);
            if (!File.Exists(_fileName))
                File.WriteAllText(_fileName, configParams);
        }

        public void Save(ConfigParams config)
        {
            var obj = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(_fileName, obj);
        }

        public ConfigParams Read()
        {
            var data = File.ReadAllText(_fileName);
            return JsonConvert.DeserializeObject<ConfigParams>(data);
        }
    }
}
