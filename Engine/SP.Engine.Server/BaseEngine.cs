using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SP.Core.Fiber;
using SP.Core.Logging;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.Logging;

namespace SP.Engine.Server;

public static class TickExtensions
{
    public static DateTime ToDateTime(this long ticks, DateTimeKind kind = DateTimeKind.Utc)
    {
        return new DateTime(ticks, kind);
    }
}

public interface IBaseEngine : ILogContext
{
    IEngineConfig Config { get; }
    IBaseSession CreateSession(TcpNetworkSession networkSession);
}

internal interface ISocketServerAccessor
{
    SocketServer SocketServer { get; }
}

public enum EServerState
{
    NotInitialized = ServerStateConst.NotInitialized,
    Initializing = ServerStateConst.Initializing,
    NotStarted = ServerStateConst.NotStarted,
    Starting = ServerStateConst.Starting,
    Running = ServerStateConst.Running,
    Stopping = ServerStateConst.Stopping
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

public abstract class BaseEngine : IBaseEngine, ISocketServerAccessor, IDisposable
{
    private readonly ConcurrentQueue<BaseSession> _authHandshakePendingQueue = new();
    private readonly ConcurrentQueue<BaseSession> _closeHandshakePendingQueue = new();
    private IDisposable _clearIdleSessionTimer;
    private IDisposable _handshakePendingTimer;
    private IDisposable _sessionSnapshotTimer;
    private ListenerInfo[] _listenerInfos;
    private SocketServer _socketServer;
    private Session[] _sessionSnapshot;
    private int _stateCode = ServerStateConst.NotInitialized;
    private bool _disposed;
    private ThreadFiber _engineFiber;
    private readonly Scheduler _globalScheduler = new();
    private SessionManager _sessionManager;
    private IDisposable _udpCleanupTimer;
    
    public string Name { get; private set; }

    protected Session[] SessionsSource
    {
        get
        {
            if (!Config.Session.EnableSessionSnapshot) return _sessionSnapshot;
            var snap = Volatile.Read(ref _sessionSnapshot);
            return snap ?? [];
        }
    }

    protected IFiber Fiber => _engineFiber;
    public ILogger Logger { get; private set; }
    public IEngineConfig Config { get; private set; }
    public IScheduler Scheduler => _globalScheduler;
    SocketServer ISocketServerAccessor.SocketServer => _socketServer;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual bool Initialize(string name, EngineConfig config)
    {
        if (Interlocked.CompareExchange(ref _stateCode, ServerStateConst.Initializing, ServerStateConst.NotInitialized)
            != ServerStateConst.NotInitialized)
            throw new InvalidOperationException(
                "The server has been initialized already, you cannot initialize it again!");

        // 프로토콜 별 정책 초기화
        ProtocolPolicyRegistry.Initialize();
        
        Name = !string.IsNullOrEmpty(name) ? name : $"{GetType().Name}-{Math.Abs(GetHashCode())}";
        Config = config ?? throw new ArgumentNullException(nameof(config));
        
        if (!SetupLogger())
            return false;

        if (!SetupListeners(config))
            return false;

        if (!SetupSocketServer())
            return false;

        _sessionManager = new SessionManager(config.Session.MaxConnections);
        _engineFiber = new ThreadFiber("EngineFiber", onError: ex => Logger.Error(ex));
        
        _stateCode = ServerStateConst.NotStarted;
        return true;
    }
    

    public virtual bool Start()
    {
        var oldState =
            Interlocked.CompareExchange(ref _stateCode, ServerStateConst.Starting, ServerStateConst.NotStarted);
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
        
        if (Config.Network.EnableUdp)
            StartUdpCleanupTimer();

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
            return;

        _socketServer?.Stop();
        _stateCode = ServerStateConst.NotStarted;

        _sessionSnapshot = null;
        StopSessionSnapshotTimer();
        StopClearIdleSessionTimer();
        StopHandshakePendingTimer();
        StopUdpCleanupTimer();
        LogManager.Dispose();

        var snapshot = _sessionManager.GetActiveSnapshot();
        if (snapshot.Length > 0)
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 };
            Parallel.ForEach(snapshot, options, s =>
            {
                try
                {
                    s.Close(CloseReason.ServerShutdown);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error closing session {0} during shutdown", s.SessionId);
                }
            });
        }

        OnStopped();

        Logger.Debug("The server instance {0} has been stopped!", Name);
    }

    public int GetOpenPort(SocketMode mode)
    {
        foreach (var info in _listenerInfos)
            if (info.Mode == mode)
                return info.EndPoint.Port;

        return -1;
    }

    public Session GetSession(long sessionId)
        => _sessionManager.GetSession(sessionId);

    public IEnumerable<Session> GetAllSessions()
    {
        var sessions = SessionsSource;
        return sessions;
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
        Logger = LogManager.GetLogger(Name);
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
        
        var listenerInfos = (from lc in config.Listeners 
            where 0 < lc.Port && (lc.Mode != SocketMode.Udp || config.Network.EnableUdp) 
            select new ListenerInfo
            { 
                EndPoint = new IPEndPoint(ParseIpAddress(lc.Ip), lc.Port), 
                BackLog = lc.BackLog, 
                Mode = lc.Mode
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



    internal virtual void OnSessionClosed(Session session, CloseReason reason)
    {
    }

    private void StartUdpCleanupTimer()
    {
        var period = TimeSpan.FromSeconds(Config.Network.UdpCleanupIntervalSec);
        _udpCleanupTimer = _globalScheduler.Schedule(_engineFiber, CleanupUdpFragment, TimeSpan.Zero, period);
    }

    private void StopUdpCleanupTimer()
    {
        _udpCleanupTimer?.Dispose();
        _udpCleanupTimer = null;
    }

    private void CleanupUdpFragment()
    {
        var sessions = SessionsSource;
        Parallel.ForEach(sessions, s => s.CleanupFragmentAssembler());
    }

    private void StartClearIdleSessionTimer()
    {
        var period = TimeSpan.FromSeconds(Config.Session.ClearIdleSessionIntervalSec);
        _clearIdleSessionTimer = _globalScheduler.Schedule(_engineFiber, ClearIdleSession, TimeSpan.Zero, period);
    }

    private void StopClearIdleSessionTimer()
    {
        _clearIdleSessionTimer?.Dispose();
        _clearIdleSessionTimer = null;
    }

    private void ClearIdleSession()
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var timeoutTicks = nowTicks - TimeSpan.FromSeconds(Config.Session.ClearIdleSessionIntervalSec).Ticks;

        foreach (var s in SessionsSource)
        {
            if (s.LastActiveTimeTicks > timeoutTicks) continue;

            if (s.IsClosed) continue;
                
            Logger.Debug(
                "The session {0} will be closed for {1} timeout, the session start time: {2}, last active time: {3}",
                s.SessionId,
                TimeSpan.FromTicks(nowTicks - s.LastActiveTimeTicks).TotalSeconds,
                s.StartTime,
                s.LastActiveTimeTicks.ToDateTime());

            s.Close(CloseReason.TimeOut);
        }
    }

    private void StartSessionSnapshotTimer()
    {
        var period = TimeSpan.FromSeconds(Config.Session.SessionSnapshotIntervalSec);
        _sessionSnapshotTimer = _globalScheduler.Schedule(_engineFiber, TakeSessionSnapshot, TimeSpan.Zero, period);
    }

    private void StopSessionSnapshotTimer()
    {
        _sessionSnapshotTimer?.Dispose();
        _sessionSnapshotTimer = null;
    }

    private void TakeSessionSnapshot()
    {
        var snapshot = _sessionManager.GetActiveSnapshot();
        Interlocked.Exchange(ref _sessionSnapshot, snapshot);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _engineFiber?.Dispose();
            _globalScheduler.Dispose();
            
            if (_stateCode == ServerStateConst.Running)
                Stop();
        }

        _disposed = true;
    }

    private void StartHandshakePendingTimer()
    {
        var period = TimeSpan.FromSeconds(Config.Session.HandshakePendingTimerIntervalSec);
        _handshakePendingTimer = _globalScheduler.Schedule(_engineFiber, ProcessPendingQueue, TimeSpan.Zero, period);
    }

    private void StopHandshakePendingTimer()
    {
        _handshakePendingTimer?.Dispose();
        _handshakePendingTimer = null;
    }

    private void ProcessPendingQueue()
    {
        if (null == _handshakePendingTimer) return;

        try
        {
            while (_authHandshakePendingQueue.TryPeek(out var session))
            {
                if (session.IsClosed || session.IsAuthenticated)
                {
                    // 인증 핸드쉐이크를 완료했거나 연결이 끊어졌으면 제거함
                    _authHandshakePendingQueue.TryDequeue(out _);
                    continue;
                }

                // 타임 아웃 체크
                if (DateTime.UtcNow < session.StartTime.AddSeconds(Config.Session.AuthHandshakeTimeoutSec))
                    continue;
                
                Logger.Debug("Timeout auth handshake for session: {0}", session.SessionId);
                
                // 인증 타임 아웃
                if (_authHandshakePendingQueue.TryDequeue(out var expired))
                    expired.Close(CloseReason.ServerClosing);
            }

            while (_closeHandshakePendingQueue.TryPeek(out var session))
            {
                if (session.IsClosed)
                {
                    // 종료 처리가 되었음
                    _closeHandshakePendingQueue.TryDequeue(out _);
                    continue;
                }

                // 타임 아웃 체크
                if (DateTime.UtcNow < session.StartClosingTime.AddSeconds(Config.Session.CloseHandshakeTimeoutSec))
                    continue;

                Logger.Debug("Timeout close handshake for session: {0}", session.SessionId);
                
                // 종료 타임아웃
                if (_closeHandshakePendingQueue.TryDequeue(out var expired))
                    expired.Close(CloseReason.ServerClosing);
            }
        }
        catch (Exception e)
        {
            Logger.Error(e);
        }
    }
    
    IBaseSession IBaseEngine.CreateSession(TcpNetworkSession ns)
    {
        var session = _sessionManager.CreateSession(this, ns);
        if (session == null) return null;
        
        ns.Session = session;
        ns.Closed += OnTcpSessionClosed;
        
        // 인증 해드쉐이크 대기 등록
        EnqueueAuthHandshakePending(session);
        Logger.Debug("A new session connected. sessionId={0}", session.SessionId);
        return session;
    }
    
    private void OnTcpSessionClosed(INetworkSession ns, CloseReason reason)
    {
        var session = ns.Session;
        if (session == null) return;

        Logger.Debug("The session {0} has been closed. reason={1}", session.SessionId, reason);
        _sessionManager.RemoveSession(session.SessionId);
        OnSessionClosed(session, reason);
    }

    private void EnqueueAuthHandshakePending(BaseSession session)
    {
        _authHandshakePendingQueue.Enqueue(session);
    }

    internal void EnqueueCloseHandshakePending(BaseSession session)
    {
        _closeHandshakePendingQueue.Enqueue(session);
    }
}
