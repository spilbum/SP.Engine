using System;

namespace SP.Engine.Common.Logging
{

    /// <summary>
    /// 로거 인터페이스
    /// </summary>
    public interface ILogger
    {
        void WriteLog(ELogLevel logLevel, string format, params object[] args);
        void WriteLog(Exception exception);
        void WriteLog(string message, Exception ex);
    }    
}

