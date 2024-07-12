using Logger.Interfaces;

namespace Logger.Config
{
    public class ConfigSeqLogger : IConfigLogger
    {
        public required string Host { get; set; }
        public int Port { get; set; }
    }
}
