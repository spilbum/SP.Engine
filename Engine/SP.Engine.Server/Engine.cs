using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SP.Core;
using SP.Core.Fiber;
using SP.Core.Logging;
using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Command;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.Connector;
using SP.Engine.Server.Logging;

namespace SP.Engine.Server;

public interface IEngine : ILogContext
{
    IEngineConfig Config { get; }
    bool Start();
    void Stop();
}

public abstract class Engine : BaseEngine, IEngine
{
    private readonly Dictionary<ushort, ICommand> _userCommands = new();
    private readonly Dictionary<ushort, ICommand> _internalCommands = new();
    private readonly List<ConnectorFiber> _connectorFibers = [];
    private readonly List<PeerFiber> _peerFibers = [];
    private PeerManager _peerManager;
    private PerfMonitor _perfMonitor;
    private ILogger _perfLogger;
    private IDisposable _waitingReconnectCheckingTimer;
    private IFiber _perfMonitorFiber;
    
    private static readonly long _baseUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public static long UtcNowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    internal static uint NetworkTimeMs => (uint)(UtcNowMs - _baseUnixMs);

    protected override bool Initialize(string name, EngineConfig config)
    {
        if (!base.Initialize(name, config))
            return false;
        
        _peerManager = new PeerManager(Logger, config);

        if (!SetupCommand())
            return false;

        if (!SetupConnectorFiber(config.Connectors))
            return false;

        SetupPeerFiber();
        SetupPerfMonitor(config.Perf);
        
        Logger.Info("The server {0} is initialized.", name);
        return true;
    }

    public override bool Start()
    {
        if (!base.Start())
            return false;
        
        foreach (var fiber in _connectorFibers)
            fiber.Start();
        
        StartReconnectTimer();
        return true;
    }

    public override void Stop()
    {
        _perfMonitor?.Dispose();
        _perfMonitorFiber?.Dispose();
        foreach (var fiber in _peerFibers) fiber.Dispose();
        foreach (var fiber in _connectorFibers) fiber.Dispose();
        StopReconnectTimer();

        base.Stop();
    }
    
    protected TPeer GetActivePeer<TPeer>(uint peerId) where TPeer : BasePeer
        => _peerManager.GetActivePeer(peerId) as TPeer;

    protected bool TransitionTo(BasePeer newPeer)
        => _peerManager.TransitionTo(newPeer);
    
    internal BasePeer GetWaitingPeer(uint peerId)
        => _peerManager.GetWaitingPeer(peerId);
    
    private PeerFiber GetPeerFiber()
        => _peerFibers.OrderBy(f => f.PeerCount).First();

    internal bool OnlinePeer(BasePeer peer, Session session)
    {
        if (!_peerManager.TransitionToOnline(peer.PeerId, session))
            return false;

        // 파이버 재할당
        var fiber = GetPeerFiber();
        fiber.AddPeer(peer);
        return true;
    }

    internal void JoinPeer(BasePeer peer)
    {
        // 파이버 할당
        var fiber = GetPeerFiber();
        fiber.AddPeer(peer);
        
        // 매니저에 피어 등록
        _peerManager.Register(peer);
    }
    
    internal bool NewPeer(ISession session, out BasePeer peer)
    {
        peer = CreatePeer(session) as BasePeer;
        return peer != null;
    }
    
    protected abstract IPeer CreatePeer(ISession session);

    internal override void OnSessionClosed(Session session, CloseReason reason)
    {
        base.OnSessionClosed(session, reason);

        var peer = session.Peer;
        if (null == peer) return;
        
        peer.OnSessionClosed(session);

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
        var ts = TimeSpan.FromSeconds(Config.Session.WaitingReconnectTimerIntervalSec);
        _waitingReconnectCheckingTimer = Scheduler.Schedule(Fiber, OnCheckWaitingReconnectCallback, ts, ts);
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
    
    private void SetupPeerFiber()
    {
        var fiberCnt = Environment.ProcessorCount;
        fiberCnt = Math.Clamp(fiberCnt, 4, 32); // todo:환경에 맞게 튜닝
        
        var tickInterval = TimeSpan.FromMilliseconds(Config.Session.PeerUpdateIntervalMs);
        
        for (var i = 0; i < fiberCnt; i++)
        {
            var fiber = new ThreadFiber($"PeerFiber_{i:D2}",
                maxBatchSize: 2048,
                capacity: 1024 * 64,
                onError: ex =>
                {
                    switch (ex)
                    {
                        case FiberException e:
                            Logger.Error(e.InnerException, "Fiber: {0}, Job: {1}", e.FiberName, e.Job?.Name ?? "unknown");
                            break;
                        case FiberQueueFullException e:
                            if (e.DroppedCount % 100 == 1)
                            {
                                Logger.Warn("Fiber '{0}' is overwhelmed. Pending: {1}, Dropped: {2}",
                                    e.FiberName, e.PendingCount, e.DroppedCount);
                            }
                            break;
                    }
                });
            
            var peerFiber = new PeerFiber(fiber, Scheduler, Logger, tickInterval);
            _peerFibers.Add(peerFiber);
        }
        
        Logger.Info("PeerFiber setup completed. FiberCount: {0}, Shared Sessions per fiber: ~{1}", 
            fiberCnt, Config.Session.MaxConnections / fiberCnt);
    }
    
    private void SetupPerfMonitor(PerfConfig config)
    {
        if (!config.MonitorEnabled)
            return;

        var fiber = new ThreadFiber("PerfMonitorFiber");
        _perfMonitorFiber = fiber;
        
        _perfMonitor = new PerfMonitor();
        Scheduler.Schedule(fiber, PerfMonitorTick, TimeSpan.Zero, config.SamplePeriod);

        if (!config.LoggerEnabled)
            return;

        _perfLogger = LogManager.GetLogger("PerfMonitor");
        Scheduler.Schedule(fiber, () =>
        {
            if (_perfMonitor.TryGetLast(out var metrics))
                _perfLogger.Info(metrics.ToString());
        }, TimeSpan.Zero, config.LoggingPeriod);
    }

    private void PerfMonitorTick()
    {
        var sessions = SessionsSource;
        _perfMonitor?.Tick(sessions.Length, _peerManager);
    }

    private bool SetupConnectorFiber(List<ConnectorConfig> configs)
    {
        foreach (var config in configs)
        {
            var fiber = new ThreadFiber($"ConnectorFiber_{config.Name}",
                onError: ex =>
                {
                    Logger.Error(ex.Message);
                });

            var cf = new ConnectorFiber(
                fiber, Scheduler, Logger,
                TimeSpan.FromMilliseconds(Config.Session.ConnectorTickPeriodMs));

            if (!cf.RegisterConnector(config, CreateConnector(config.Name)))
                return false;
            
            _connectorFibers.Add(cf);
        }

        return true;
    }
    
    protected abstract IConnector CreateConnector(string name);

    private bool SetupCommand()
    {
        try
        {
            RegisterInternalCommand<SessionAuth>(C2SEngineProtocolId.SessionAuthReq);
            RegisterInternalCommand<Close>(C2SEngineProtocolId.Close);
            RegisterInternalCommand<Ping>(C2SEngineProtocolId.Ping);
            RegisterInternalCommand<MessageAck>(C2SEngineProtocolId.MessageAck);
            RegisterInternalCommand<UdpHelloReq>(C2SEngineProtocolId.UdpHelloReq);
            RegisterInternalCommand<UdpHealthCheckReq>(C2SEngineProtocolId.UdpHealthCheckReq);
            RegisterInternalCommand<UdpHealthCheckConfirm>(C2SEngineProtocolId.UdpHealthCheckConfirm);

            var assembly = GetType().Assembly;
            DiscoverUserCommands(assembly);
        
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
        var commandTypes = assembly.GetTypes();
        var peerTypes = commandTypes.Where(t => typeof(IPeer).IsAssignableFrom(t)).ToList();

        foreach (var t in commandTypes)
        {
            if (!t.IsClass || t.IsAbstract) continue;
            if (!typeof(ICommand).IsAssignableFrom(t)) continue;
            
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
        var peer = session.Peer;

        using (message)
        {
            if (message is TcpMessage { SequenceNumber: > 0 } tcp && peer != null)
            {
                var messages = peer.IngestReceivedMessage(tcp);
                if (messages == null) return;

                foreach (var m in messages)
                {
                    using (m) DispatchCommand(session, peer, m);
                }
            }
            else
            {
                DispatchCommand(session, peer, message);
            }
        }
    }

    private void DispatchCommand(BaseSession session, BasePeer peer, IMessage message)
    {
        var internalCommand = GetInternalCommand(message.Id);
        if (internalCommand != null)
        {
            internalCommand.Execute(session, message);
            return;
        }

        if (peer == null)
            return;
        
        var command = GetUserCommand(message.Id);
        if (command == null)
            return;

        var protocol = command.Deserialize(peer, message);
        if (protocol == null)
        {
            peer.Close(CloseReason.Rejected);
            return;
        }

        peer.EnqueueJob(command.Execute, peer, protocol);
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
