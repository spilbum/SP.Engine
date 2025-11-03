namespace SP.Core.Logging
{
    public class ConsoleLoggerFactory : ILoggerFactory
    {
        public ILogger GetLogger(string category)
        {
            return new ConsoleLogger(category);
        }
    }
}
