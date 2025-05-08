using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SP.Common.Logging;
using SP.Engine.Runtime;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.Logging;

namespace SP.Engine.Server
{
    public interface IEngine : ILogContext
    {
        IEngineConfig Config { get; }
        bool Initialize(string name, IEngineConfig config);
        bool Start();
        void Stop();
        ISession CreateSession(ISocketSession socketSession);   
        bool RegisterSession(ISession session);
    }

    public interface ISocketServerAccessor
    {
        ISocketServer SocketServer { get; }
    }

    public enum EServerState
    {
        NotInitialized = ServerStateConst.NotInitialized,
        Initializing = ServerStateConst.Initializing,
        NotStarted = ServerStateConst.NotStarted,
        Starting = ServerStateConst.Starting,
        Running = ServerStateConst.Running,
        Stopping = ServerStateConst.Stopping,
    }

    public struct ServerStateConst
    {
        public const int NotInitialized = 0;
        public const int Initializing = 1;
        public const int NotStarted = 2;
        public const int Starting = 3;
        public const int Running = 4;
        public const int Stopping = 5;
    }

    public abstract class BaseEngine<TSession> : IEngine, ISocketServerAccessor, IDisposable
        where TSession : BaseSession<TSession>, ISession, new()
    {
        private SocketServer _socketServer;
        private ListenerInfo[] _listenerInfos;
        private int _stateCode = ServerStateConst.NotInitialized;

        ISocketServer ISocketServerAccessor.SocketServer => _socketServer;
        public string Name { get; private set; }
        public ILogger Logger { get; private set; }
        public IEngineConfig Config { get; private set; }
        
        public virtual bool Initialize(string name, IEngineConfig config)
        {
            if (Interlocked.CompareExchange(ref _stateCode, ServerStateConst.Initializing, ServerStateConst.NotInitialized)
                != ServerStateConst.NotInitialized)
            {
                throw new InvalidOperationException("The server has been initialized already, you cannot initialize it again!");
            }

            Name = !string.IsNullOrEmpty(name) ? name : $"{GetType().Name}-{Math.Abs(GetHashCode())}";
            Config = config ?? throw new ArgumentNullException(nameof(config));

            if (!SetupLogger())
                return false;

            if (!SetupListeners(config))
                return false;

            if (!SetupSocketServer())
                return false;

            _stateCode = ServerStateConst.NotStarted;                
            return true;
        }

        public virtual bool Start()
        {
            var oldState = Interlocked.CompareExchange(ref _stateCode, ServerStateConst.Starting, ServerStateConst.NotStarted);
            if (oldState != ServerStateConst.NotStarted)
            {
                Logger.Fatal("This server instance is in the state {0}, you cannot start it now.", (EServerState)oldState);
                return false;
            }

            if (null == _socketServer || !_socketServer.Start())
            {
                _stateCode = ServerStateConst.NotStarted;
                return false;
            }

            _stateCode = ServerStateConst.Running;

            if (!Config.IsDisableSessionSnapshot)
                StartSessionsSnapshotTimer();

            if (!Config.IsDisableClearIdleSession)
                StartClearIdleSessionTimer();

            StartHandshakePendingQueueTimer();
            
            try
            {
                OnStarted();
            }
            catch (Exception ex)
            {
                Logger.Fatal("An exception occurred in the method 'OnStarted()': {0}", ex.Message);
            }

            Logger.Info("The server instance {0} was been started!", Name);
            return true;
        }

        public virtual void Stop()
        {
            if (Interlocked.CompareExchange(ref _stateCode, ServerStateConst.Stopping, ServerStateConst.Running)
                != ServerStateConst.Running)
            {
                return;
            }

            _socketServer?.Stop();
            _stateCode = ServerStateConst.NotStarted;

            _sessionsSnapshot = null;
            StopSessionsSnapshotTimer();
            StopClearIdleSessionTimer();
            StopHandshakePendingQueueTimer();
            LogManager.Dispose();
            
            var sessions = _sessionDict.ToArray();
            if (0 < sessions.Length)
            {
                var tasks = sessions.Select(s => Task.Run(() =>
                {
                    s.Value.Close(ECloseReason.ServerShutdown);
                })).ToArray();

                Task.WaitAll(tasks);
            }

            OnStopped();

            Logger.Debug("The server instance {0} has been stopped!", Name);
        }

        public TSession GetSession(string sessionId)
        {
            _sessionDict.TryGetValue(sessionId, out var session);
            return session;
        }

        public IEnumerable<TSession> GetAllSessions()
        {
            var sessions = SessionsSource;
            return sessions.Select(x => x.Value);
        }

        protected virtual void OnStarted()
        {

        }

        protected virtual void OnStopped()
        {

        }

        private bool SetupLogger()
        {
            var loggerFactory = new SerilogFactory();
            LogManager.SetLoggerFactory(loggerFactory);
            LogManager.SetDefaultCategory(Name);

            Logger = LogManager.GetLogger();
            Logger?.Info("Logger setup was successful: {0}", Name);
            return true;
        }

        private bool SetupSocketServer()
        {
            if (null == _listenerInfos)
            {
                Logger.Fatal("ListenerInfos is null");
                return false;
            }

            _socketServer = new SocketServer(this, _listenerInfos);
            return true;
        }

        private bool SetupListeners(IEngineConfig config)
        {
            if (null == config.Listeners || 0 == config.Listeners.Count)
            {
                Logger.Fatal("Listeners is null or empty in server config.");
                return false;
            }

            var listenerInfos = (from l in config.Listeners 
                where 0 < l.Port 
                select new ListenerInfo
                {
                    EndPoint = new IPEndPoint(ParseIpAddress(l.Ip), l.Port), 
                    BackLog = l.BackLog, 
                    Mode = l.Mode
                }).ToList();

            if (0 == listenerInfos.Count)
            {
                Logger.Fatal("No listener defined.");
                return false;
            }

            _listenerInfos = [..listenerInfos];
            return true;
        }

        private static IPAddress ParseIpAddress(string ip)
        {
            if (string.IsNullOrEmpty(ip) || "Any".Equals(ip, StringComparison.OrdinalIgnoreCase))
                return IPAddress.Any;
            else if ("IPv6Any".Equals(ip, StringComparison.OrdinalIgnoreCase))
                return IPAddress.IPv6Any;
            else
                return IPAddress.Parse(ip);
        }

        ISession IEngine.CreateSession(ISocketSession socketSession)
        {
            var session = new TSession();
            session.Initialize(this, socketSession);
            return session;
        }

        bool IEngine.RegisterSession(ISession session)
        {
            if (session is not TSession tSession)
                return false;

            if (!_sessionDict.TryAdd(tSession.SessionId, tSession))
            {
                Logger.Error("The session is refused because the it's ID already exists. sessionId={0}", tSession.SessionId);
                return false;
            }

            tSession.SocketSession.Closed += OnSocketSessionClosed;

            // 인증 해드쉐이크 대기 등록
            EnqueueAuthHandshakePendingQueue(tSession);
            Logger.Debug("A new session connected. sessionId={0}, remoteEndPoint={1}", tSession.SessionId, tSession.RemoteEndPoint);
            return true;
        }

        private void OnSocketSessionClosed(ISocketSession socketSession, ECloseReason reason)
        {
            if (socketSession.Session is not TSession session) 
                return;
         
            session.IsConnected = false;
            OnSessionClosed(session, reason);
        }

        protected virtual void OnSessionClosed(TSession session, ECloseReason reason)
        {
            if (!_sessionDict.TryRemove(session.SessionId, out var removed))
            {
                Logger.Error("Failed to remove this session, because it hasn't been in session container.");
                return;
            }
            
            Logger.Debug("The session {0} has been closed. reason={1}", removed.SessionId, reason);
        }

        private Timer _clearIdleSessionTimer;
        private readonly object _clearIdleSessionLock = new object();

        private void StartClearIdleSessionTimer()
        {
            var intervalMs = Config.ClearIdleSessionIntervalSec * 1000;
            _clearIdleSessionTimer = new Timer(ClearIdleSession, null, intervalMs, intervalMs);
        }

        private void StopClearIdleSessionTimer()
        {
            if (null == _clearIdleSessionTimer) 
                return;
            
            _clearIdleSessionTimer.Dispose();
            _clearIdleSessionTimer = null;
            Logger.Debug("Timer stopped.");
        }

        private void ClearIdleSession(object state)
        {
            if (!Monitor.TryEnter(_clearIdleSessionLock))
                return; 

            try
            {
                var source = SessionsSource;
                var config = Config;
                var now = DateTime.UtcNow;
                
                // 비활성된 시간 체크
                var sessions = source
                    .Where(x => x.Value.LastActiveTime <= now.AddSeconds(-config.IdleSessionTimeOutSec))
                    .Select(x => x.Value);
                
                var options = new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount };
                Parallel.ForEach(sessions, options, session =>
                {
                    Logger.Debug(
                        "The session {0} will be closed for {1} timeout, the session start time: {2}, last active time: {3}",
                        session.SessionId,
                        now.Subtract(session.LastActiveTime).TotalSeconds,
                        session.StartTime,
                        session.LastActiveTime);

                    session.Close(ECloseReason.TimeOut);
                });
            }
            catch (Exception ex)
            {
                Logger.Error("Clear idle session error: {0}\n{1}", ex.Message, ex.StackTrace);
            }
            finally
            {
                Monitor.Exit(_clearIdleSessionLock);
            }
        }

        protected KeyValuePair<string, TSession>[] SessionsSource
        {
            get
            {
                if (Config.IsDisableSessionSnapshot)
                    return [.. _sessionDict];
                else
                    return Interlocked.CompareExchange(ref _sessionsSnapshot, null, null) ?? [];
            }
        }

        private Timer _sessionsSnapshotTimer;
        private KeyValuePair<string, TSession>[] _sessionsSnapshot;
        private readonly object _snapshotLock = new object();        
        private readonly ConcurrentDictionary<string, TSession> _sessionDict = new(Environment.ProcessorCount, 3000, StringComparer.OrdinalIgnoreCase);        

        private void StartSessionsSnapshotTimer()
        {
            _sessionsSnapshotTimer = new Timer(TakeSessionsSnapshot, null, TimeSpan.FromSeconds(Config.SessionsSnapshotIntervalSec), TimeSpan.FromSeconds(Config.SessionsSnapshotIntervalSec));
        }

        private void StopSessionsSnapshotTimer()
        {
            _sessionsSnapshotTimer?.Dispose();
            _sessionsSnapshotTimer = null;
        }

        private void TakeSessionsSnapshot(object state)
        {
            if (!Monitor.TryEnter(_snapshotLock))
                return; 

            try
            { 
                Interlocked.Exchange(ref _sessionsSnapshot, _sessionDict.ToArray());
            }
            finally
            {
                Monitor.Exit(_snapshotLock);
            }
        }

        private bool _disposed;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) 
                return;
            
            if (disposing)
            {
                if (_stateCode == ServerStateConst.Running)
                    Stop();
            }

            _disposed = true;
        }

        private Timer _handshakePendingQueueTimer;
        private readonly ConcurrentQueue<TSession> _authHandshakePendingQueue = new ConcurrentQueue<TSession>();
        private readonly ConcurrentQueue<TSession> _closeHandshakePendingQueue = new ConcurrentQueue<TSession>();
        
        private void StartHandshakePendingQueueTimer()
        {
            _handshakePendingQueueTimer = 
                new Timer(CheckHandshakePendingQueueCallback
                    , null
                    , TimeSpan.FromSeconds(Config.HandshakePendingQueueTimerIntervalSec)
                    , TimeSpan.FromSeconds(Config.HandshakePendingQueueTimerIntervalSec));
        }

        private void StopHandshakePendingQueueTimer()
        {
            _handshakePendingQueueTimer?.Dispose();
            _handshakePendingQueueTimer = null;
        }

        private void CheckHandshakePendingQueueCallback(object state)
        {
            if (null == _handshakePendingQueueTimer)
                return;
            
            _handshakePendingQueueTimer.Change(Timeout.Infinite, Timeout.Infinite);   
            
            try
            {   
                while (_authHandshakePendingQueue.TryPeek(out var session))
                {
                    if (session.IsAuthorized || !session.IsConnected)
                    {
                        // 인증 받았거나 연결이 끊어졌으면 제거함
                        _authHandshakePendingQueue.TryDequeue(out _);
                        continue;
                    }
                
                    // 타임 아웃 체크
                    if (DateTime.UtcNow < session.StartTime.AddSeconds(Config.AuthHandshakeTimeOutSec))
                        continue;
                
                    // 인증 타임 아웃
                    _authHandshakePendingQueue.TryDequeue(out _);
                    session.Close(ECloseReason.TimeOut);
                }
            
                while (_closeHandshakePendingQueue.TryPeek(out var session))
                {
                    if (!session.IsConnected)
                    {
                        // 종료 처리가 되었음
                        _closeHandshakePendingQueue.TryDequeue(out _);
                        continue;
                    }

                    // 타임 아웃 체크
                    if (DateTime.UtcNow < session.StartClosingTime.AddSeconds(Config.CloseHandshakeTimeOutSec))
                        continue;

                    Logger.Debug("Client terminated due to timeout. sessionId={0}", session.SessionId);
                    
                    // 종료 타임아웃
                    _closeHandshakePendingQueue.TryDequeue(out _);
                    session.Close(ECloseReason.ServerClosing);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
            finally
            {
                _handshakePendingQueueTimer.Change(TimeSpan.FromSeconds(Config.HandshakePendingQueueTimerIntervalSec), TimeSpan.FromSeconds(Config.HandshakePendingQueueTimerIntervalSec));
            }
        }

        private void EnqueueAuthHandshakePendingQueue(TSession session)
        {
            _authHandshakePendingQueue.Enqueue(session);
        }
        
        internal void EnqueueCloseHandshakePendingQueue(TSession session)
        {
            _closeHandshakePendingQueue.Enqueue(session);
        }
    }
}
