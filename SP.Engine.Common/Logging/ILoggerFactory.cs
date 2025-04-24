namespace SP.Engine.Common.Logging
{
    public interface ILoggerFactory
    {
        ILogger GetLogger(string category);
    }    
}

