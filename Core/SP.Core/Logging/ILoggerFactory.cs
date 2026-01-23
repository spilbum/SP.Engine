namespace SP.Core.Logging
{
    public interface ILoggerFactory
    {
        ILogger GetLogger(string category);
    }
}
