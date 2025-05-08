namespace SP.Common.Logging
{
    public interface ILoggerFactory
    {
        ILogger GetLogger(string category);
    }    
}

