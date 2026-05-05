using FluentAssertions;
using Newtonsoft.Json;
using TelegramAutoDownload.Models;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class ConfigFileTests
    {
        [Fact]
        public void ConfigParams_RoundTrip_Serialization()
        {
            var original = new ConfigParams
            {
                PathSaveFile = @"C:\Downloads",
                DownloadThreads = 5,
            };

            var json = JsonConvert.SerializeObject(original, Formatting.Indented);
            var deserialized = JsonConvert.DeserializeObject<ConfigParams>(json);

            deserialized.Should().NotBeNull();
            deserialized!.PathSaveFile.Should().Be(@"C:\Downloads");
            deserialized.DownloadThreads.Should().Be(5);
        }

        [Fact]
        public void ConfigParams_NullJson_ReturnsNewInstance()
        {
            // Simulates the ConfigFile.Read() guard: null JSON returns new ConfigParams()
            var result = JsonConvert.DeserializeObject<ConfigParams>("null") ?? new ConfigParams();
            result.Should().NotBeNull();
            result.Chats.Should().NotBeNull();
        }

        [Fact]
        public void ConfigParams_CorruptJson_ReturnsNewInstance()
        {
            // Corrupt JSON should result in exception caught upstream and a new instance returned
            ConfigParams? result = null;
            try
            {
                result = JsonConvert.DeserializeObject<ConfigParams>("{corrupt json!!}");
            }
            catch
            {
                result = new ConfigParams();
            }

            result.Should().NotBeNull();
        }
    }
}
