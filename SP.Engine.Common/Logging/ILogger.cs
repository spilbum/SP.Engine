using System;

namespace SP.Engine.Common.Logging
{
    public interface ILogger
    {
        void Log(ELogLevel level, string message);
        void Log(ELogLevel level, string format, params object[] args);
        void Log(ELogLevel level, Exception ex);
        void Log(ELogLevel level, Exception ex, string format, params object[] args);

        void Debug(string message);
        void Debug(string format, params object[] args);

        void Info(string message);
        void Info(string format, params object[] args);

        void Warn(string message);
        void Warn(string format, params object[] args);

        void Error(string message);
        void Error(string format, params object[] args);
        void Error(Exception ex);
        void Error(Exception ex, string format, params object[] args);

        void Fatal(string message);
        void Fatal(string format, params object[] args);
        void Fatal(Exception ex);
        void Fatal(Exception ex, string format, params object[] args);
    }    
}

