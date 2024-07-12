using Logger.Config;
using Serilog;

namespace Logger.Services
{
    public class SeqService
    {
        private readonly Serilog.Core.Logger logger;
        public SeqService(ConfigSeqLogger configLog)
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
