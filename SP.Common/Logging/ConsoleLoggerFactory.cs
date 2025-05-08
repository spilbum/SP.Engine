
namespace SP.Common.Logging
{
    public class ConsoleLoggerFactory : ILoggerFactory
    {
        public ILogger GetLogger(string category) => new ConsoleLogger(category);
    }
}
