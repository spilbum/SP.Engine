using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using SP.Core.Fiber;
using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Command;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.Connector;

namespace SP.Engine.Server;

public abstract class EngineBase : EngineCore
{
    private readonly Dictionary<ushort, ICommand> _userCommands = new();
    private readonly Dictionary<ushort, ICommand> _internalCommands = new();
    private readonly List<ConnectorFiber> _connectorFibers = [];

    private ThreadFiber[] _logicFibers;
    private List<PeerBase>[] _shardPeers;
    private IDisposable[] _shardTickTimers;
    private int _shardMask;
    
    private PeerManager _peerManager;
    private PerfMonitor _perfMonitor;
    private IDisposable _waitingReconnectCheckingTimer;
    private ThreadFiber _perfMonitorFiber;
    
    private static readonly ConcurrentBag<ThreadPerfLog> _threadPerfLogs = [];
    [ThreadStatic] private static ThreadPerfLog _threadPerfLog;
    [ThreadStatic] private static List<TcpMessage> _orderCache;

    private class ThreadPerfLog
    {
        public long ProcessedCount;
        public double TotalExecutionTimeMs;
    }

    private static ThreadPerfLog GetCurrentThreadPerfLog()
    {
        if (_threadPerfLog != null) return _threadPerfLog;
        _threadPerfLog = new ThreadPerfLog();
        _threadPerfLogs.Add(_threadPerfLog);
        return _threadPerfLog;
    }
    
    public int GetLogicFiberPendingCount(int index)
    {
        if (_logicFibers == null || index >= _logicFibers.Length) return 0;
        return _logicFibers[index].QueuePendingCount;
    }
    
    public int LogicFiberCount => _logicFibers.Length;
    
    private static readonly long _baseUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static long UtcNowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    internal static uint NetworkTimeMs => (uint)(UtcNowMs - _baseUnixMs);

    internal override bool InternalInitialize(Assembly[] assemblies, string name, EngineConfig config)
    {
        if (!base.InternalInitialize(assemblies, name, config))
            return false;
        
        _peerManager = new PeerManager(Logger, config);
        
        if (!SetupCommand(assemblies))
            return false;

        if (!SetupConnectorFiber(assemblies, config.Connectors))
            return false;
        
        SetupLogicFibers();
        
        Logger.Info("The server {0} is initialized.", name);
        return true;
    }

    internal override bool InternalStart()
    {
        if (!base.InternalStart())
            return false;
        
        foreach (var fiber in _connectorFibers)
            fiber.Start();
        
        StartReconnectTimer();
        StartPerfMonitor(Config.Perf);
        
        try
        {
            OnStarted();
        }
        catch (Exception ex)
        {
            Logger.Fatal("An exception occurred in the method 'OnStarted()': {0}", ex.Message);
            return false;
        }

        return true;
    }

    internal override void InternalStop()
    {
        _perfMonitor?.Dispose();
        _perfMonitorFiber?.Dispose();

        if (_shardTickTimers != null)
        {
            foreach (var timer in _shardTickTimers) timer?.Dispose();
        }

        if (_logicFibers != null)
        {
            foreach (var fiber in _logicFibers) fiber?.Dispose();
        }
        
        foreach (var fiber in _connectorFibers) fiber.Dispose();
        StopReconnectTimer();

        base.InternalStop();
        OnStopped();
    }
    
    protected abstract IPeer CreatePeer(Session session);
    protected abstract IConnector CreateConnector(string name);
    protected virtual void OnStarted() { }
    protected virtual void OnStopped() { }
    
    public bool Start() => InternalStart();
    public void Stop() => InternalStop();
    
    protected TPeer GetActivePeer<TPeer>(uint peerId) where TPeer : PeerBase
        => _peerManager.GetActivePeer(peerId) as TPeer;

    protected bool TransitionTo(PeerBase newPeer)
        => _peerManager.TransitionTo(newPeer);
    
    internal PeerBase GetWaitingPeer(uint peerId)
        => _peerManager.GetWaitingPeer(peerId);

    private int GetShardIndex(uint peerId) => (int)(peerId & _shardMask);

    internal bool OnlinePeer(PeerBase peer, Session session)
    {
        if (!_peerManager.TransitionToOnline(peer.PeerId, session))
            return false;

        RegisterPeerToShard(peer);
        return true;
    }

    internal void JoinPeer(PeerBase peer)
    {
        _peerManager.Register(peer);
        RegisterPeerToShard(peer);
    }

    private void RegisterPeerToShard(PeerBase peer)
    {
        var index = GetShardIndex(peer.PeerId);
        var fiber = _logicFibers[index];
        fiber.Enqueue(_shardPeers[index].Add, peer);
    }

    private void UnregisterPeerFromShard(PeerBase peer)
    {
        var index = GetShardIndex(peer.PeerId);
        var fiber = _logicFibers[index];

        fiber.Enqueue(() =>
        {
            _shardPeers[index].Remove(peer);
        });
    }
    
    internal bool NewPeer(Session session, out PeerBase peer)
    {
        peer = CreatePeer(session) as PeerBase;
        return peer != null;
    }
    
    protected override void OnSessionClosed(Session session, CloseReason reason)
    {
        var peer = session.Peer;
        if (null == peer) return;

        UnregisterPeerFromShard(peer);

        if (ShouldKeepPeer(session))
        {
            // 재 연결 대기로 전환
            _peerManager.TransitionToOffline(peer, reason);
        }
        else
        {
            // 즉시 제거
            _peerManager.Terminate(peer.PeerId, reason);
        }
    }

    private static bool ShouldKeepPeer(Session session)
    {
        if (session.IsClosing) return false;
        return session.Peer is { Kind: PeerKind.User };
    }
    
    private void StartReconnectTimer()
    {
        var ts = TimeSpan.FromSeconds(Config.Session.WaitingReconnectTimerPeriodSec);
        _waitingReconnectCheckingTimer = GlobalScheduler.Schedule(Fiber, OnCheckWaitingReconnectCallback, ts, ts);
    }

    private void StopReconnectTimer()
    {
        _waitingReconnectCheckingTimer?.Dispose();
        _waitingReconnectCheckingTimer = null;
    }

    private void OnCheckWaitingReconnectCallback()
    {
        try
        {
            _peerManager.Update();
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error checking reconnect timeouts");
        }
    }

    private void SetupLogicFibers()
    {
        var coreCount = Environment.ProcessorCount;
        var fiberCount = 1;
        while (fiberCount < coreCount) fiberCount <<= 1;

        fiberCount = Math.Clamp(fiberCount, 4, 32);
        _shardMask = fiberCount - 1;
        
        _logicFibers = new ThreadFiber[fiberCount];
        _shardPeers = new List<PeerBase>[fiberCount];
        _shardTickTimers = new IDisposable[fiberCount];

        var interval = TimeSpan.FromMilliseconds(Config.Session.PeerUpdateIntervalMs);

        for (var index = 0; index < fiberCount; index++)
        {
            _logicFibers[index] = new ThreadFiber($"LogicFiber-{index:D2}",
                capacity: 4096,
                maxBatchSize: 512,
                onError: OnLogicFiberException);

            _shardPeers[index] = [];
            _shardTickTimers[index] = GlobalScheduler.Schedule(
                _logicFibers[index],
                ProcessFiberPeersTick,
                index,
                TimeSpan.Zero,
                interval);
        }
        
        Logger.Info("LogicFiber pool setup completed. FiberCount: {0}", fiberCount);
    }

    private void ProcessFiberPeersTick(int index)
    {
        var peers = _shardPeers[index];
        for (var i = peers.Count - 1; i >= 0; i--)
        {
            try
            {
                peers[i].Tick();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Peer tick failed in Fiber-{0}: PeerId {1}", index, peers[i].PeerId);
            }
        }
    }

    private void OnLogicFiberException(Exception ex)
    {
        if (ex is FiberException e)
        {
            Logger.Error(e.InnerException, "LogicFiber '{0}' execute failed. Job={1}", e.FiberName, e.Job?.Name);
        }
    }

    private void StartPerfMonitor(PerfConfig config)
    {
        if (!config.MonitorEnabled) return;

        _perfMonitorFiber =  new ThreadFiber("PerfMonitorFiber");
        _perfMonitor = new PerfMonitor();

        // 수집 루프 시작
        _perfMonitorFiber.Enqueue(PerfMonitorTickLoop);
        // 로깅 루프 시작
        _perfMonitorFiber.Enqueue(PerfMonitorLoggingLoop);
    }

    private void PerfMonitorLoggingLoop()
    {
        try
        {
            if (_perfMonitor != null && _perfMonitor.TryGetLast(out var metrics))
                Logger.Info(metrics.ToString());
        }
        finally
        {
            // 다음 루프 예약
            GlobalScheduler.Schedule(
                _perfMonitorFiber,
                PerfMonitorLoggingLoop,
                TimeSpan.FromSeconds(Config.Perf.LoggingPeriodSec),
                TimeSpan.Zero);
        }
    }

    private void PerfMonitorTickLoop()
    {
        try
        {
            long totalProcessed = 0;
            double totalTimeMs = 0;
            foreach (var log in _threadPerfLogs)
            {
                totalProcessed += Volatile.Read(ref log.ProcessedCount);
                totalTimeMs += Volatile.Read(ref log.TotalExecutionTimeMs);
            }

            var sessions = SessionsSource;
            _perfMonitor?.Tick(this, sessions.Length, totalProcessed, totalTimeMs);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to execute performance monitor tick.");
        }
        finally
        {
            // 다음 루프 예약
            GlobalScheduler.Schedule(
                _perfMonitorFiber,
                PerfMonitorTickLoop,
                TimeSpan.FromSeconds(1),
                TimeSpan.Zero);
        }
    }

    private bool SetupConnectorFiber(Assembly[] assemblies, List<ConnectorConfig> configs)
    {
        foreach (var config in configs)
        {
            var fiber = new ThreadFiber($"ConnectorFiber-{config.Name}",
                onError: ex =>
                {
                    Logger.Error(ex.Message);
                });

            var cf = new ConnectorFiber(
                fiber, GlobalScheduler, Logger,
                TimeSpan.FromMilliseconds(Config.Session.ConnectorUpdateIntervalMs));

            if (!cf.RegisterConnector(assemblies, config, CreateConnector(config.Name)))
                return false;
            
            _connectorFibers.Add(cf);
        }

        return true;
    }

    private bool SetupCommand(Assembly[] assemblies)
    {
        try
        {
            // 엔진 명령어 등록
            RegisterInternalCommand<SessionAuth>(C2SEngineProtocolId.SessionAuthReq);
            RegisterInternalCommand<Close>(C2SEngineProtocolId.Close);
            RegisterInternalCommand<Ping>(C2SEngineProtocolId.Ping);
            RegisterInternalCommand<MessageAck>(C2SEngineProtocolId.MessageAck);
            RegisterInternalCommand<UdpHelloReq>(C2SEngineProtocolId.UdpHelloReq);
            RegisterInternalCommand<UdpHealthCheckConfirm>(C2SEngineProtocolId.UdpHealthCheckConfirm);

            // 유저 명령어 추출
            foreach (var assembly in assemblies)
            {
                DiscoverUserCommands(assembly);    
            }
            
            return true;
        }
        catch (Exception e)
        {
            Logger.Error(e);
            return false;
        }
    }

    private void RegisterInternalCommand<T>(ushort protocolId) where T : ICommand, new()
        => _internalCommands[protocolId] = new T();

    private void DiscoverUserCommands(Assembly assembly)
    {
        var commandTypes = assembly.GetTypes()
            .Where(t => typeof(ICommand).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
            .ToList();
        
        var peerTypes = assembly.GetTypes()
            .Where(t => typeof(IPeer).IsAssignableFrom(t))
            .ToList();

        if (commandTypes.Count == 0 || peerTypes.Count == 0)
            return;

        foreach (var t in commandTypes)
        {
            var attr = t.GetCustomAttribute<ProtocolCommandAttribute>();
            if (attr == null)
            {
                throw new InvalidDataException($"[{t.FullName}] requires {nameof(ProtocolCommandAttribute)}");
            }
  
            if (Activator.CreateInstance(t) is not ICommand command) continue;
            if (!peerTypes.Contains(command.ContextType)) continue;
            if (!_userCommands.TryAdd(attr.Id, command))
            {
                throw new InvalidDataException($"Duplicate command: {attr.Id}");
            }
        }

        Logger.Debug("[Engine] Discovered '{0}' commands: [{1}]", _userCommands.Count, string.Join(", ", _userCommands.Keys));
    }

    private ICommand GetInternalCommand(ushort protocolId)
    {
        _internalCommands.TryGetValue(protocolId, out var command);
        return command;
    }

    private ICommand GetUserCommand(ushort protocolId)
    {
        _userCommands.TryGetValue(protocolId, out var command);
        return command;
    }
    
    internal void ExecuteCommand(Session session, IMessage message)
    {
        if (session == null) return;
        
        try
        {
            var command = GetInternalCommand(message.Id);
            if (command != null)
            {
                command.Execute(session, message);
                return;
            }
            
            var peer = session.Peer;
            if (peer == null) return;
            
            if (message is TcpMessage { SequenceNumber: > 0 } tcp)
            {
                _orderCache ??= new List<TcpMessage>(32);
                _orderCache.Clear();

                var result = peer.ReceiveIngestMessage(tcp, _orderCache);
                switch (result)
                {
                    case ReceiveIngestResult.Success:
                    {
                        foreach (var m in _orderCache)
                        {
                            using (m) DispatchUserCommand(peer, m);
                        }
                        break;
                    }
                    case ReceiveIngestResult.BufferOverflow:
                    {
                        Logger.Warn("Peer {0} Out-of-order buffer overflow (Msx: {1})"
                            , peer.PeerId, Config.Network.ReliableMaxOutOfOrderCount);

                        peer.Close(CloseReason.Rejected);
                        return;
                    }
                    case ReceiveIngestResult.Buffered:
                    case ReceiveIngestResult.Duplicate:
                    default:
                        break;
                }
            }
            else
            {
                DispatchUserCommand(peer, message);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "ExecuteCommand failed: {0}", ex.Message);
        }
        finally
        {
            message.Dispose();
        }
    }

    private void DispatchUserCommand(PeerBase peer, IMessage message)
    {
        IMessage newMessage;
        
        // 메시지 소유권 이전
        switch (message)
        {
            case TcpMessage tcp:
                newMessage = tcp.Extract();
                break;
            case UdpMessage udp:
                newMessage = udp.Extract();
                break;
            default:
                return;
        }
        
        var index = GetShardIndex(peer.PeerId);
        var logicFiber = _logicFibers[index];
        logicFiber.Enqueue(ExecuteUseCommand, this, peer, newMessage);
    }

    private static void ExecuteUseCommand(EngineBase engine, PeerBase peer, IMessage message)
    {
        var command = engine.GetUserCommand(message.Id);
        if (command == null) return;

        double executionTimeMs;
        
        try
        {
            executionTimeMs = command.Execute(peer, message);
        }
        finally
        {
            message.Dispose();
        }
        
        var log = GetCurrentThreadPerfLog();
        log.ProcessedCount++;
        log.TotalExecutionTimeMs += executionTimeMs;

        if (executionTimeMs >= engine.Config.Session.CommandSlowThresholdMs)
        {
            engine.Logger.Warn(
                "Command '{0}' slow detected. PeerId={1}, Exec={2:F2}ms", command.Name, peer.PeerId, executionTimeMs);
        }
    }

    public IEnumerable<IConnector> GetAllConnectors()
    {
        return _connectorFibers.Select(fiber => fiber.Connector);
    }

    public IEnumerable<IConnector> GetConnectors(string name)
    {
        return _connectorFibers.Where(c => c.Name.Equals(name)).Select(c => c.Connector);
    }

    public IConnector GetConnector(string name, string host, int port)
    {
        return _connectorFibers
            .Where(c => c.Name.Equals(name) && c.Host.Equals(host) && c.Port.Equals(port))
            .Select(c => c.Connector)
            .FirstOrDefault();
    }
}
