using Serilog;
using Serilog.Events;

namespace Logger
{
    public class Logger
    {
        private readonly Serilog.Core.Logger logger;

        public Logger(ConfigLog configLog)
        {
            logger = new LoggerConfiguration()
          .WriteTo.Seq($"{configLog.Host}:{configLog.Port}")
          .CreateLogger();
        }
        public Serilog.Core.Logger GetInstance()
        {
            return logger;
        }
    }
}
