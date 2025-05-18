using System.Collections.Generic;
using System.Threading;

namespace SP.Engine.Server.Configuration
{
    public interface IEngineConfig
    {
        int SendTimeOutMs { get; }
        int LimitConnectionCount { get; }
        int ReceiveBufferSize { get; }
        int SendBufferSize { get; }
        bool IsDisableClearIdleSession { get; }
        int ClearIdleSessionIntervalSec { get; }
        int IdleSessionTimeOutSec { get; }
        int MaxAllowedLength { get; }
        bool IsDisableSessionSnapshot { get; }
        int SessionsSnapshotIntervalSec { get; }
        int KeepAliveTimeSec { get; }
        int KeepAliveIntervalSec { get; }
        int SendingQueueSize { get; }
        bool IsLogAllSocketError { get; }
        List<ListenerConfig> Listeners { get; }
        int WaitingReconnectPeerTimeOutSec { get; }
        int WaitingReconnectPeerTimerIntervalSec { get; }
        int HandshakePendingQueueTimerIntervalSec { get; }
        int AuthHandshakeTimeOutSec { get; }
        int CloseHandshakeTimeOutSec { get; }
        int MaxReSendCnt { get; }
        List<ConnectorConfig> Connectors { get; } 
        
        int MaxWorkingThreads { get; }
        int MinWorkingThreads { get; }
        int MaxCompletionPortThreads { get; }
        int MinCompletionPortThreads { get; }
    }
    public class EngineConfig : IEngineConfig
    {
        private const int DefaultReceiveBufferSize = 4 * 1024;
        private const int DefaultLimitConnectionCount = 100;
        private const int DefaultSendingQueueSize = 5;
        private const int DefaultLimitRequestLength = 4096;
        private const int DefaultSendTimeOutMs = 5000;
        private const int DefaultClearIdleSessionIntervalSec = 120;
        private const int DefaultIdleSessionTimeOutSec = 300;
        private const int DefaultSendBufferSize = 4096;
        private const int DefaultSessionSnapshotIntervalSec = 1;
        private const int DefaultKeepAliveTimeSec = 10;
        private const int DefaultKeepAliveIntervalSec = 2;
        private const int DefaultWaitingReconnectPeerTimeOutSec = 120;
        private const int DefaultWaitingReconnectPeerTimerIntervalSec = 60;
        private const int DefaultHandshakePendingQueueTimerIntervalSec = 60;
        private const int DefaultAuthHandshakeTimeOutSec = 120;
        private const int DefaultCloseHandshakeTimeOutSec = 120;
        private const int DefaultMaxReSendCnt = 5;
        
        public int SendTimeOutMs { get; set; }
        public int LimitConnectionCount { get; set; }
        public int ReceiveBufferSize { get; set; }
        public int SendBufferSize { get; set; }
        public bool IsDisableClearIdleSession { get; set; }
        public int ClearIdleSessionIntervalSec { get; set; }
        public int IdleSessionTimeOutSec { get; set; }
        public int MaxAllowedLength { get; set; }
        public bool IsDisableSessionSnapshot { get; set; }
        public int SessionsSnapshotIntervalSec { get; set; }
        public int KeepAliveTimeSec { get; set; }
        public int KeepAliveIntervalSec { get; set; }
        public int SendingQueueSize { get; set; }
        public bool IsLogAllSocketError { get; set; }
        public List<ListenerConfig> Listeners { get; set; } = new List<ListenerConfig>();
        public int WaitingReconnectPeerTimeOutSec { get; set; }
        public int WaitingReconnectPeerTimerIntervalSec { get; set; }
        public int HandshakePendingQueueTimerIntervalSec { get; set; }
        public int AuthHandshakeTimeOutSec { get; set; }
        public int CloseHandshakeTimeOutSec { get; set; }
        public int MaxReSendCnt { get; set; }
        public List<ConnectorConfig> Connectors { get; set; } = new List<ConnectorConfig>();
        
        public int MaxWorkingThreads { get; set; }
        public int MinWorkingThreads { get; set; }
        public int MaxCompletionPortThreads { get; set; }
        public int MinCompletionPortThreads { get; set; }
        
        public EngineConfig()
        {
            ThreadPool.GetMaxThreads(out var maxWorkingThreads, out var maxCompletionPortThreads);
            MaxWorkingThreads = maxWorkingThreads;
            MaxCompletionPortThreads = maxCompletionPortThreads;
            ThreadPool.GetMinThreads(out var minWorkingThreads, out var minCompletionPortThreads);
            MinWorkingThreads = minWorkingThreads;
            MinCompletionPortThreads = minCompletionPortThreads;

            LimitConnectionCount = DefaultLimitConnectionCount;
            MaxAllowedLength = DefaultLimitRequestLength;
            KeepAliveTimeSec = DefaultKeepAliveTimeSec;
            KeepAliveIntervalSec = DefaultKeepAliveIntervalSec;
            ReceiveBufferSize = DefaultReceiveBufferSize;
            SendingQueueSize = DefaultSendingQueueSize;
            SendTimeOutMs = DefaultSendTimeOutMs;
            ClearIdleSessionIntervalSec = DefaultClearIdleSessionIntervalSec;
            IdleSessionTimeOutSec = DefaultIdleSessionTimeOutSec;
            SendBufferSize = DefaultSendBufferSize;
            SessionsSnapshotIntervalSec = DefaultSessionSnapshotIntervalSec;
            WaitingReconnectPeerTimeOutSec = DefaultWaitingReconnectPeerTimeOutSec;
            WaitingReconnectPeerTimerIntervalSec = DefaultWaitingReconnectPeerTimerIntervalSec;
            HandshakePendingQueueTimerIntervalSec = DefaultHandshakePendingQueueTimerIntervalSec;
            AuthHandshakeTimeOutSec = DefaultAuthHandshakeTimeOutSec;
            CloseHandshakeTimeOutSec = DefaultCloseHandshakeTimeOutSec;
            MaxReSendCnt = DefaultMaxReSendCnt;
        }
    }
}
