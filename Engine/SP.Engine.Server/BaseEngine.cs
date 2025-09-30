﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SP.Common.Fiber;
using SP.Common.Logging;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Networking;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.Logging;

namespace SP.Engine.Server
{
    public interface IBaseEngine : ILogContext
    {
        IEngineConfig Config { get; }
        IBaseSession CreateSession(TcpNetworkSession networkSession);   
        bool RegisterSession(IBaseSession session);
        void ProcessUdpClient(byte[] buffer, Socket socket, IPEndPoint remoteEndPoint);
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

    public abstract class BaseEngine<TSession> : IBaseEngine, ISocketServerAccessor, IDisposable
        where TSession : BaseSession<TSession>, IBaseSession, new()
    {
        private SocketServer _socketServer;
        private ListenerInfo[] _listenerInfos;
        private int _stateCode = ServerStateConst.NotInitialized;

        ISocketServer ISocketServerAccessor.SocketServer => _socketServer;
        public string Name { get; private set; }
        public ILogger Logger { get; private set; }
        public IEngineConfig Config { get; private set; }
        public abstract IFiberScheduler Scheduler { get; }
        
        protected abstract IBasePeer GetBasePeer(PeerId peerId);
        
        public virtual bool Initialize(string name, EngineConfig config)
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
                Logger.Fatal("Failed to start socket server.");
                _stateCode = ServerStateConst.NotStarted;
                return false;
            }

            _stateCode = ServerStateConst.Running;

            if (Config.Session.EnableSessionSnapshot)
                StartSessionSnapshotTimer();

            if (Config.Session.EnableClearIdleSession)
                StartClearIdleSessionTimer();

            StartHandshakePendingTimer();
            
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

            _sessionSnapshot = null;
            StopSessionSnapshotTimer();
            StopClearIdleSessionTimer();
            StopHandshakePendingTimer();
            LogManager.Dispose();
            
            var sessions = _sessionDict.ToArray();
            if (0 < sessions.Length)
            {
                var tasks = sessions.Select(s => Task.Run(() =>
                {
                    s.Value.Close(CloseReason.ServerShutdown);
                })).ToArray();

                Task.WaitAll(tasks);
            }

            OnStopped();

            Logger.Debug("The server instance {0} has been stopped!", Name);
        }

        public int GetOpenPort(ESocketMode mode)
        {
            foreach (var info in _listenerInfos)
            {
                if (info.Mode == mode)
                    return info.EndPoint.Port;
            }

            return -1;
        }

        public IBaseSession GetSession(string sessionId)
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
            LogManager.Initialize(Name, new SerilogFactory());
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

        private bool SetupListeners(EngineConfig config)
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
            return "IPv6Any".Equals(ip, StringComparison.OrdinalIgnoreCase) ? IPAddress.IPv6Any : IPAddress.Parse(ip);
        }

        IBaseSession IBaseEngine.CreateSession(TcpNetworkSession networkSession)
        {
            var session = new TSession();
            session.Initialize(this, networkSession);
            return session;
        }

        bool IBaseEngine.RegisterSession(IBaseSession session)
        {
            if (session is not TSession s)
                return false;

            if (!_sessionDict.TryAdd(s.SessionId, s))
            {
                Logger.Error("The session is refused because the it's ID already exists. sessionId={0}", s.SessionId);
                return false;
            }

            session.NetworkSession.Closed += OnNetworkSessionClosed;

            // 인증 해드쉐이크 대기 등록
            EnqueueAuthHandshakePending(s);
            Logger.Debug("A new session connected. sessionId={0}, remoteEndPoint={1}", s.SessionId, s.RemoteEndPoint);
            return true;
        }

        void IBaseEngine.ProcessUdpClient(byte[] buffer, Socket socket, IPEndPoint remoteEndPoint)
        {
            if (!UdpHeader.TryParse(buffer, out var header))
            {
                Logger.Warn("Invalid UDP header found.");
                return;
            }

            var peer = GetBasePeer(header.PeerId);
            var session = peer?.BaseSession;
            if (session == null)
            {
                Logger.Warn("Not found session. peerId={0}", header.PeerId);
                return;
            }
            
            session.ProcessBuffer(buffer, header, socket, remoteEndPoint);
        }

        private void OnNetworkSessionClosed(INetworkSession networkSession, CloseReason reason)
        {
            if (networkSession.Session is not TSession session) 
                return;
         
            session.IsConnected = false;
            OnSessionClosed(session, reason);
        }

        protected virtual void OnSessionClosed(TSession session, CloseReason reason)
        {
            if (!_sessionDict.TryRemove(session.SessionId, out var removed))
            {
                Logger.Error("Failed to remove this session, because it hasn't been in session container.");
                return;
            }
            
            Logger.Debug("The session {0} has been closed. reason={1}", removed.SessionId, reason);
        }

        private Timer _clearIdleSessionTimer;
        private readonly object _clearIdleSessionLock = new();

        private void StartClearIdleSessionTimer()
        {
            var intervalMs = Config.Session.ClearIdleSessionIntervalSec * 1000;
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
                    .Where(x => x.Value.LastActiveTime <= now.AddSeconds(-config.Session.IdleSessionTimeoutSec))
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

                    session.Close(CloseReason.TimeOut);
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
                if (!Config.Session.EnableSessionSnapshot) return _sessionSnapshot;
                var snap = Volatile.Read(ref _sessionSnapshot);
                return snap ?? [];

            }
        }

        private Timer _sessionSnapshotTimer;
        private KeyValuePair<string, TSession>[] _sessionSnapshot;
        private readonly object _snapshotLock = new();        
        private readonly ConcurrentDictionary<string, TSession> _sessionDict = new(Environment.ProcessorCount, 3000, StringComparer.OrdinalIgnoreCase);        

        private void StartSessionSnapshotTimer()
        {
            var ts = TimeSpan.FromSeconds(Config.Session.SessionSnapshotIntervalSec);
            _sessionSnapshotTimer = new Timer(TakeSessionSnapshot, null, ts, ts);
        }

        private void StopSessionSnapshotTimer()
        {
            _sessionSnapshotTimer?.Dispose();
            _sessionSnapshotTimer = null;
        }

        private void TakeSessionSnapshot(object state)
        {
            if (!Monitor.TryEnter(_snapshotLock))
                return; 

            try
            { 
                Interlocked.Exchange(ref _sessionSnapshot, _sessionDict.ToArray());
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

        private Timer _handshakePendingTimer;
        private readonly ConcurrentQueue<TSession> _authHandshakePendingQueue = new();
        private readonly ConcurrentQueue<TSession> _closeHandshakePendingQueue = new();

        private void StartHandshakePendingTimer()
        {
            var ts = TimeSpan.FromSeconds(Config.Session.HandshakePendingTimerIntervalSec);
            _handshakePendingTimer = new Timer(CheckHandshakePendingCallback, null, ts, ts);
        }

        private void StopHandshakePendingTimer()
        {
            _handshakePendingTimer?.Dispose();
            _handshakePendingTimer = null;
        }

        private void CheckHandshakePendingCallback(object state)
        {
            if (null == _handshakePendingTimer)
                return;
            
            _handshakePendingTimer.Change(Timeout.Infinite, Timeout.Infinite);   
            
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
                    if (DateTime.UtcNow < session.StartTime.AddSeconds(Config.Session.AuthHandshakeTimeoutSec))
                        continue;
                
                    // 인증 타임 아웃
                    _authHandshakePendingQueue.TryDequeue(out _);
                    session.Close(CloseReason.TimeOut);
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
                    if (DateTime.UtcNow < session.StartClosingTime.AddSeconds(Config.Session.CloseHandshakeTimeoutSec))
                        continue;

                    Logger.Debug("Client terminated due to timeout. sessionId={0}", session.SessionId);
                    
                    // 종료 타임아웃
                    _closeHandshakePendingQueue.TryDequeue(out _);
                    session.Close(CloseReason.ServerClosing);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
            finally
            {
                var ts = TimeSpan.FromSeconds(Config.Session.HandshakePendingTimerIntervalSec);
                _handshakePendingTimer.Change(ts, ts);
            }
        }

        private void EnqueueAuthHandshakePending(TSession session)
        {
            _authHandshakePendingQueue.Enqueue(session);
        }
        
        internal void EnqueueCloseHandshakePending(TSession session)
        {
            _closeHandshakePendingQueue.Enqueue(session);
        }
    }
}
