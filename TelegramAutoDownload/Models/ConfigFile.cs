using Newtonsoft.Json;
using System.IO;

namespace TelegramAutoDownload.Models
{
    public class ConfigFile
    {
        private readonly string _fileName;

        /// <param name="filePath">
        /// Custom path for the config file. Defaults to <see cref="AppPaths.ConfigFile"/>.
        /// Pass a temp path in tests to avoid touching the real config on disk.
        /// </param>
        public ConfigFile(string? filePath = null)
        {
            _fileName = filePath ?? AppPaths.ConfigFile;
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
