using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using SP.Core.Fibers;
using SP.Engine.Common.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Networking;
using SP.Engine.Server.Command;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.S2S;
using SP.Engine.Server.S2S.Command;

namespace SP.Engine.Server;

public interface IEngine
{
    string Category { get; }
    string Name { get; }
    ServerState State { get; }
    bool Start();
    void Stop();
}

public abstract class EngineBase : EngineCore, IEngine
{
    private static readonly long _baseUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static long UtcNowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    internal static uint NetworkTimeMs => (uint)(UtcNowMs - _baseUnixMs);
    
    private readonly Dictionary<ushort, ICommandHandler> _appCommands = new();
    private readonly Dictionary<ushort, ICommandHandler> _engineCommands = new();
    private readonly ConcurrentDictionary<string, S2SClientGroup> _s2sClientGroups = [];
    private PeerManager _peerManager;
    private PerfMonitor _perfMonitor;
    private IDisposable _waitingReconnectCheckingTimer;
    private ThreadFiber _perfFiber;
    
    private ThreadFiber[] _logicFibers;
    private IDisposable[] _shardTickTimers;
    private Dictionary<uint, PeerBase>[] _shardPeers;
    private int _shardMask;
    
    private static readonly ConcurrentBag<ThreadPerfLog> _threadPerfLogs = [];
    [ThreadStatic] private static ThreadPerfLog _threadPerfLog;
    [ThreadStatic] private static List<TcpMessage> _orderCache;

    internal int LogicFiberCount => _logicFibers.Length;
    
    private class ThreadPerfLog
    {
        public long ProcessedCount;
        public long TotalExecutionTimeMs;
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
    
    internal override bool InternalInitialize(Assembly[] assemblies, string category, string name, EngineConfig config)
    {
        if (!base.InternalInitialize(assemblies, category, name, config))
            return false;
        
        _peerManager = new PeerManager(config);

        if (!SetupCommand(assemblies))
            return false;

        if (!SetupS2SClients(assemblies, config.S2SClients))
            return false;
        
        SetupLogicFibers(config);

        Logger.Info("The server {0} is initialized.", name);
        return true;
    }

    internal override bool InternalStart()
    {
        if (!base.InternalStart())
            return false;
        
        foreach (var group in _s2sClientGroups.Values) group.Start();
        
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
        _perfFiber?.Dispose();

        if (_shardTickTimers != null)
        {
            foreach (var timer in _shardTickTimers) timer?.Dispose();
        }

        if (_logicFibers != null)
        {
            foreach (var fiber in _logicFibers) fiber?.Dispose();
        }
        
        foreach (var group in _s2sClientGroups.Values) group.Dispose();
        StopReconnectTimer();

        base.InternalStop();
        OnStopped();
    }

    protected abstract S2SPeerBase OnCreateS2SPeer(Session session, string cateogry);
    protected abstract PeerBase OnCreatePeer(Session session);
    protected virtual void OnStarted() { }
    protected virtual void OnStopped() { }
    
    public bool Start() => InternalStart();
    public void Stop() => InternalStop();

    protected int FindActivePeers<TPeer>(List<TPeer> destination) where TPeer : PeerBase
        => _peerManager.FindActivePeers(destination);
    
    protected TPeer GetActivePeer<TPeer>(uint peerId) where TPeer : PeerBase
        => _peerManager.GetActivePeer<TPeer>(peerId);

    internal PeerBase GetWaitingPeer(uint peerId)
        => _peerManager.GetWaitingPeer(peerId);

    private ThreadFiber GetLogicFiber(uint peerId)
    {
        var index = GetShardIndex(peerId);
        return _logicFibers[index];
    }

    private int GetShardIndex(uint peerId) => (int)(peerId & _shardMask);
    
    private void AddShardPeer(PeerBase peer)
    {
        var index = GetShardIndex(peer.PeerId);
        _shardPeers[index].TryAdd(peer.PeerId, peer);
    }

    private void RemoveShardPeer(uint peerId)
    {
        var index = GetShardIndex(peerId);
        _shardPeers[index].Remove(peerId);
    }

    internal bool ActivatePeer(PeerBase peer, Session session)
    {
        if (!_peerManager.TransitionToOnline(peer.PeerId, session))
            return false;
        
        var fiber = GetLogicFiber(peer.PeerId);
        fiber.Enqueue(AddShardPeer, peer);
        return true;
    }

    internal bool JoinPeer(PeerBase peer)
    {
        if (!_peerManager.Register(peer)) return false;
        
        var index = GetShardIndex(peer.PeerId);
        if (_shardPeers[index].ContainsKey(peer.PeerId)) return false;

        _logicFibers[index].Enqueue(AddShardPeer, peer);
        return true;
    }

    internal bool NewPeer(Session session, out PeerBase peer)
    {
        peer = OnCreatePeer(session);
        return peer != null;
    }

    internal S2SConnectResult ConnectS2SPeer(Session session, string category)
    {
        if (session.Peer is not S2SPendingPeer pendingPeer) return S2SConnectResult.AlreadyConnected;
        
        var newPeer = OnCreateS2SPeer(session, category);
        if (newPeer == null) return S2SConnectResult.InternalError;

        newPeer.InheritSecurityContext(pendingPeer);
        pendingPeer.Dispose();
        
        if (!JoinPeer(newPeer)) return S2SConnectResult.InternalError;
        
        OnS2SPeerConnected(newPeer);
        return S2SConnectResult.Success;
    }
    
    protected virtual void OnS2SPeerConnected(S2SPeerBase peer)
    {
        
    }
    
    protected override void OnSessionClosed(Session session, CloseReason reason)
    {
        var peer = session.Peer;
        if (peer == null) return;
        
        switch (peer)
        {
            case S2SPendingPeer pendingPeer:
                pendingPeer.Dispose();
                break;
            default:
            {
                var fiber = GetLogicFiber(peer.PeerId);
                fiber.Enqueue(RemoveShardPeer, peer.PeerId);

                if (session.IsClosing)
                {
                    // 종료 중이면 즉시 제거
                    _peerManager.RemovePeer(peer.PeerId, reason);
                    return;
                }
        
                // 오프라인 전환
                _peerManager.TransitionToOffline(peer, reason);
                break;
            }
        }
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

    private void SetupLogicFibers(EngineConfig config)
    {
        var coreCount = Environment.ProcessorCount;
        var fiberCount = 1;
        while (fiberCount < coreCount) fiberCount <<= 1;
        fiberCount = Math.Clamp(fiberCount, 4, 32);
        
        _logicFibers = new ThreadFiber[fiberCount];
        _shardTickTimers = new IDisposable[fiberCount];
        _shardPeers = new Dictionary<uint, PeerBase>[fiberCount];
        _shardMask = fiberCount - 1;
        
        for (var index = 0; index < fiberCount; index++)
        {
            _logicFibers[index] = new ThreadFiber($"LogicFiber-{index:D2}",
                capacity: 4096,
                maxBatchSize: 512,
                onError: OnLogicFiberException);

            _shardPeers[index] = [];
            _shardTickTimers[index] = GlobalScheduler.Schedule(
                _logicFibers[index],
                UpdatePeersTick,
                index,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(config.Session.PeerUpdateIntervalMs));
        }
        
        Logger.Info("LogicFiber setup completed. FiberCount: {0}", fiberCount);
    }

    private void UpdatePeersTick(int index)
    {
        foreach (var kvp in _shardPeers[index])
        {
            try
            {
                kvp.Value.Tick();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to update peer: {0}", kvp.Key);
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

        _perfFiber = new ThreadFiber("PerfMonitorFiber");
        _perfMonitor = new PerfMonitor();

        // 수집 루프 시작
        _perfFiber.Enqueue(PerfMonitorTickLoop);
        // 로깅 루프 시작
        _perfFiber.Enqueue(PerfMonitorLoggingLoop);
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
                _perfFiber,
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

            _perfMonitor?.Tick(this, totalProcessed, totalTimeMs);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to execute performance monitor tick.");
        }
        finally
        {
            // 다음 루프 예약
            GlobalScheduler.Schedule(
                _perfFiber,
                PerfMonitorTickLoop,
                TimeSpan.FromSeconds(1),
                TimeSpan.Zero);
        }
    }

    private bool SetupS2SClients(Assembly[] assemblies, List<S2SClientConfig> configs)
    {
        foreach (var config in configs)
        {
            var s2sClient = new S2SClient(this);
            if (!s2sClient.Initialize(assemblies, config))
            {
                Logger.Fatal("S2SClient '{0}' initialize failed.", config.Name);
                return false;
            }

            s2sClient.Connected += OnS2SClientConnected;
            s2sClient.Disconnected += OnS2SClientDisconnected;
            
            var group = _s2sClientGroups.GetOrAdd(s2sClient.Name, name => new S2SClientGroup(name));
            group.AddClient(s2sClient);
        }

        return true;
    }

    protected virtual void OnS2SClientConnected(S2SClient client)
    {
    }

    protected virtual void OnS2SClientDisconnected(S2SClient client)
    {
        
    }

    private bool SetupCommand(Assembly[] assemblies)
    {
        try
        {
            // 엔진 명령어 등록
            RegisterEngineCommand<SessionAuthReqHandler>(ProtocolId.C2S.SessionAuthReq);
            RegisterEngineCommand<CloseCmdHandler>(ProtocolId.C2S.CloseCmd);
            RegisterEngineCommand<PingHandler>(ProtocolId.C2S.Ping);
            RegisterEngineCommand<MessageAckHandler>(ProtocolId.C2S.MessageAck);
            RegisterEngineCommand<UdpHelloReqHandler>(ProtocolId.C2S.UdpHelloReq);
            RegisterEngineCommand<UdpHealthCheckAckHandler>(ProtocolId.C2S.UdpHealthCheckAck);
            RegisterEngineCommand<S2SConnectReqHandler>(ProtocolId.S2S.S2SConnectReq);

            // 사용자 명령어 추출
            foreach (var assembly in assemblies)
            {
                DiscoverAppCommands(assembly);    
            }
            
            return true;
        }
        catch (Exception e)
        {
            Logger.Error(e);
            return false;
        }
    }

    private void RegisterEngineCommand<T>(ushort protocolId) where T : ICommandHandler, new()
        => _engineCommands[protocolId] = new T();

    private void DiscoverAppCommands(Assembly assembly)
    {
        var commandHandlerTypes = assembly.GetTypes()
            .Where(t => typeof(ICommandHandler).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
            .ToList();
        
        var peerTypes = assembly.GetTypes()
            .Where(t => typeof(IPeer).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
            .ToList();
        
        if (commandHandlerTypes.Count == 0 || peerTypes.Count == 0)
            return;

        foreach (var t in commandHandlerTypes)
        {
            var attr = t.GetCustomAttribute<CommandHandlerAttribute>();
            if (attr == null)
            {
                throw new InvalidDataException($"[{t.FullName}] requires {nameof(CommandHandlerAttribute)}");
            }
  
            if (Activator.CreateInstance(t) is not ICommandHandler command) continue;
            if (!peerTypes.Contains(command.ContextType)) continue;
            if (!_appCommands.TryAdd(attr.Id, command))
            {
                throw new InvalidDataException($"Duplicate command: {attr.Id}");
            }
        }

        Logger.Debug("[Engine] Discovered '{0}' commands: [{1}]", _appCommands.Count, string.Join(", ", _appCommands.Keys));
    }

    private ICommandHandler GetEngineCommand(ushort protocolId)
    {
        _engineCommands.TryGetValue(protocolId, out var command);
        return command;
    }

    private ICommandHandler GetAppCommand(ushort protocolId)
    {
        _appCommands.TryGetValue(protocolId, out var command);
        return command;
    }
    
    internal void ExecuteCommand(Session session, IMessage message)
    {
        if (session == null) return;
        
        try
        {
            // 내부 명령어 실행
            var command = GetEngineCommand(message.Id);
            if (command != null)
            {
                using (message) command.Execute(session, message);
                return;
            }
            
            var peer = session.Peer;
            if (peer == null)
            {
                message.Dispose();
                return;
            }

            lock (peer)
            {
                if (message is TcpMessage { SequenceNumber: > 0 } tcp)
                {
                    _orderCache ??= new List<TcpMessage>(32);
                    _orderCache.Clear();

                    var result = peer.ReceiveIngestMessage(tcp, _orderCache);
                    switch (result)
                    {
                        case ReceiveIngestResult.Success:
                        {
                            var index = 0;
                            try
                            {
                                for (; index < _orderCache.Count; index++)
                                {
                                    var m = _orderCache[index];
                                    using (m) DispatchAppCommand(peer, m.Extract());
                                }
                            }
                            finally
                            {
                                for (; index < _orderCache.Count; index++)
                                {
                                    _orderCache[index].Dispose();
                                }
                            }
                            break;
                        }
                        case ReceiveIngestResult.BufferOverflow:
                            Logger.Warn("Peer {0} Out-of-order buffer overflow.", peer.PeerId);
                            peer.Close(CloseReason.Rejected);
                            tcp.Dispose();
                            break;
                        case ReceiveIngestResult.Buffered:
                            break;
                        case ReceiveIngestResult.Duplicate:
                        default:
                            tcp.Dispose();
                            break;
                    }
                }
                else
                {
                    IMessage extracted = message switch
                    {
                        TcpMessage t => t.Extract(),
                        UdpMessage u => u.Extract(),
                        _ => null
                    };
                    
                    using (message) DispatchAppCommand(peer, extracted);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "ExecuteCommand failed: {0}", ex.Message);
            message.Dispose();
        }
    }

    private void DispatchAppCommand(PeerBase peer, IMessage message)
    {
        if (message == null) return;
        var fiber = GetLogicFiber(peer.PeerId);
        fiber.Enqueue(ExecuteAppCommand, this, peer, message);
    }

    private static void ExecuteAppCommand(EngineBase engine, PeerBase peer, IMessage message)
    {
        try
        {
            var command = engine.GetAppCommand(message.Id);
            if (command == null) return;

            var elapsedTicks = command.Execute(peer, message);
        
            var log = GetCurrentThreadPerfLog();
            Interlocked.Increment(ref log.ProcessedCount);
            Interlocked.Add(ref log.TotalExecutionTimeMs, elapsedTicks);
            if (elapsedTicks >= engine.Config.Session.CommandSlowThresholdMs)
            {
                engine.Logger.Warn(
                    "Command '{0}' slow detected. PeerId={1}, Exec={2:F2}ms", command.Name, peer.PeerId, elapsedTicks);
            }
        }
        finally
        {
            message.Dispose();
        }

    }

    public IS2SClient GetAvailableS2SClient(string name)
    {
        return _s2sClientGroups.TryGetValue(name, out var group) 
            ? group.GetAvailableClient() 
            : null;
    }
    
    public IEnumerable<IS2SClient> GetAvailableS2SClients(string name)
    {
        return _s2sClientGroups.TryGetValue(name, out var group)
            ? group.GetAllAvailableClients()
            : [];
    }
}
