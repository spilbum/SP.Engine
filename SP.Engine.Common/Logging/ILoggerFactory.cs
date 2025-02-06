namespace SP.Engine.Common.Logging
{

    /// <summary>
    /// 로거 팩토리 인터페이스
    /// </summary>
    public interface ILoggerFactory
    {
        ILogger GetLogger(string name);
    }    
}

