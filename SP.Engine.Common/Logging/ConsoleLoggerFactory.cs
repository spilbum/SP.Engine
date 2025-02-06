
namespace SP.Engine.Common.Logging
{
    public class ConsoleLoggerFactory : ILoggerFactory
    {
        public ILogger GetLogger(string name) => new ConsoleLogger(name);
    }
}
