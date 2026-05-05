using BasePlugins;
using MonoTorrent;
using MonoTorrent.Client;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TorrentPlugin
{
    public class TorrentPlugin<TMessage> : BasePlugin<TMessage>
    {
        public override string PluginName => "Torrent";
        public override int Priority => 3;

        public override bool CanHandle(Config config)
        {
            return config.Text.StartsWith("magnet:");
        }

        public override async Task<ResultExecute> ExecuteAsync(Config config)
        {
            var outputFolder = Path.Combine(config.PathSaveFile, "Torrent", config.ChatName);
            if (!Directory.Exists(outputFolder))
                Directory.CreateDirectory(outputFolder);

            try
            {
                var settingsBuilder = new EngineSettingsBuilder
                {
                    CacheDirectory = Path.Combine(outputFolder, ".cache"),
                };
                var settings = settingsBuilder.ToSettings();

                using var engine = new ClientEngine(settings);

                var magnetLink = MagnetLink.Parse(config.Text);
                var torrentManager = await engine.AddAsync(magnetLink, outputFolder);

                await torrentManager.StartAsync();

                // Wait for download to complete or fail (with timeout)
                using var cts = new CancellationTokenSource(TimeSpan.FromHours(24));
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(2000, cts.Token);

                    var state = torrentManager.State;
                    if (state == TorrentState.Seeding || state == TorrentState.Stopped)
                        break;
                    if (state == TorrentState.Error)
                    {
                        await torrentManager.StopAsync();
                        return new ResultExecute(config.ChatName)
                        {
                            IsSuccess = false,
                            ErrorMessage = $"Torrent error: {torrentManager.Error?.Exception?.Message}"
                        };
                    }
                }

                await torrentManager.StopAsync();

                var downloadedName = torrentManager.Torrent?.Name
                    ?? torrentManager.Files.FirstOrDefault()?.Path
                    ?? magnetLink.Name
                    ?? "torrent";

                return new ResultExecute(config.ChatName)
                {
                    IsSuccess = true,
                    FileName = downloadedName
                };
            }
            catch (Exception ex)
            {
                return new ResultExecute(config.ChatName)
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }
    }
}
