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
    private readonly List<BaseConnector> _connectors = [];
    private readonly List<PeerFiber> _peerFibers = [];
    private PeerManager _peerManager;
    private PerfMonitor _perfMonitor;
    private ILogger _perfLogger;
    private IDisposable _waitingReconnectCheckingTimer;

    public override bool Initialize(string name, EngineConfig config)
    {
        if (!base.Initialize(name, config))
            return false;
        
        _peerManager = new PeerManager(Logger, config);

        if (!SetupCommand())
            return false;

        if (!SetupConnector(config.Connectors))
            return false;

        SetupPerfMonitor(config.Perf);
        
        Logger.Info("The server {0} is initialized.", name);
        return true;
    }

    public override bool Start()
    {
        if (!base.Start())
            return false;

        var connectorUpdatePeriod = TimeSpan.FromMilliseconds(Config.Session.ConnectorUpdateIntervalMs);
        Scheduler.Schedule(Fiber, ScheduleUpdateConnectors, connectorUpdatePeriod, connectorUpdatePeriod);

        var fiberCnt = Math.Max(Config.Network.LimitConnectionCount / 300, 10);
        var tickInterval = TimeSpan.FromMilliseconds(Config.Session.PeerUpdateIntervalMs);
        
        for (var i = 0; i < fiberCnt; i++)
        {
            var fiber = new ThreadFiber($"PeerFiber_{i:D2}", maxBatchSize: 1024, capacity: 4096,
                onError: ex =>
                {
                    Logger.Error(ex);
                });
            
            var peerFiber = new PeerFiber(fiber, Scheduler, Logger, tickInterval);
            _peerFibers.Add(peerFiber);
        }
        
        foreach (var connector in _connectors) 
            connector.Connect();
        
        StartReconnectTimer();
        return true;
    }

    public override void Stop()
    {
        _perfMonitor?.Dispose();
        foreach (var fiber in _peerFibers) fiber.Dispose();
        foreach (var connector in _connectors) connector.Close();
        StopReconnectTimer();

        base.Stop();
    }
    
    protected TPeer GetPeer<TPeer>(uint peerId) where TPeer : BasePeer
        => GetBasePeer(peerId) as TPeer;

    protected override BasePeer GetBasePeer(uint peerId)
        => _peerManager.GetPeer(peerId);

    protected bool ChangeServerPeer(BasePeer newPeer)
        => _peerManager.ChangeServerPeer(newPeer);
    
    internal BasePeer GetWaitingReconnectPeer(uint peerId)
        => _peerManager.GetWaitingPeer(peerId);
    
    private PeerFiber GetPeerFiber()
        => _peerFibers.OrderBy(f => f.PeerCount).First();

    internal bool OnlinePeer(BasePeer peer, ISession session)
    {
        if (!_peerManager.Online(peer, session))
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
        _peerManager.Join(peer);
    }
    
    internal bool NewPeer(ISession session, out BasePeer peer)
    {
        peer = CreatePeer(session) as BasePeer;
        return peer != null;
    }
    
    protected abstract IPeer CreatePeer(ISession session);

    protected virtual void OnPeerRemoved(IPeer peer, CloseReason reason)
    {
        
    }
    
    internal override void OnSessionClosed(Session session, CloseReason reason)
    {
        base.OnSessionClosed(session, reason);

        var peer = session.Peer;
        if (null == peer) return;
        
        peer.Fiber.RemovePeer(peer);
        
        if (session.IsClosing)
        {
            // 종료중이면 즉시 제거
            _peerManager.RemovePeer(peer, reason);
            OnPeerRemoved(peer, reason);
            return;
        }
        
        if (peer.IsConnected)
        {
            // 오프라인 전환
            _peerManager.Offline(peer, reason);
            return;
        }
        
        _peerManager.RemovePeer(peer, reason);
        OnPeerRemoved(peer, reason);
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
            _peerManager.CheckTimeouts();
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error checking reconnect timeouts");
        }
    }
    
    private void SetupPerfMonitor(PerfConfig config)
    {
        if (!config.MonitorEnabled)
            return;

        _perfMonitor = new PerfMonitor();
        Scheduler.Schedule(Fiber, _perfMonitor.Tick, TimeSpan.Zero, config.SamplePeriod);

        if (!config.LoggerEnabled)
            return;

        _perfLogger = LogManager.GetLogger("PerfMonitor");
        Scheduler.Schedule(Fiber, () =>
        {
            if (_perfMonitor.TryGetLast(out var metrics))
                _perfLogger.Info(metrics.ToString());
        }, TimeSpan.Zero, config.LoggingPeriod);
    }

    private bool SetupConnector(List<ConnectorConfig> connectors)
    {
        foreach (var config in connectors)
        {
            try
            {
                if (CreateConnector(config.Name) is not BaseConnector connector)
                {
                    Logger.Info("Failed to create connector. name={0}.", config.Name);
                    return false;
                }

                var fiber = new ThreadFiber($"ConnectorFiber_{config.Name}",
                    onError: ex =>
                    {
                        Logger.Error(ex.Message);
                    });
                
                if (!connector.Initialize(config, fiber, Scheduler, Logger))
                    throw new InvalidOperationException($"Failed to initialize connector. name={connector.Name}.");

                _connectors.Add(connector);
            }
            catch (Exception e)
            {
                Logger.Error(e);
                return false;
            }
        }

        return true;
    }
    
    private void ScheduleUpdateConnectors()
    {
        try
        {
            foreach (var connector in _connectors)
                connector.Update();
        }
        catch (Exception e)
        {
            Logger.Error(e);
        }
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
            RegisterInternalCommand<UdpKeepAlive>(C2SEngineProtocolId.UdpKeepAlive);

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

    internal void ExecuteMessage(Session session, IMessage message)
    {
        var command = GetInternalCommand(message.Id);
        if (command != null)
        {
            command.Execute(session, message);
            return;
        }
        
        command = GetUserCommand(message.Id);
        if (command == null)
        {
            if (message is TcpMessage tcp)
                session.SendMessageAck(tcp.SequenceNumber);
            
            Logger.Error("Unknown command: msgId={0}, session={1}/{2}",
                message.Id, session.Id, session.RemoteEndPoint);
            return;
        }
        
        var peer = session.Peer;
        if (peer == null)
        {
            Logger.Warn("Peer not authenticated. sessionId={0}", session.Id);
            session.Close();
            return;
        }

        switch (message)
        {
            case TcpMessage tcp:
                session.SendMessageAck(tcp.SequenceNumber);
                foreach (var msg in peer.ProcessMessageInOrder(tcp))
                {
                    var cmd = GetUserCommand(msg.Id);
                    cmd?.Execute(peer, msg);
                }
                break;
            case UdpMessage udp:
                command.Execute(peer, udp);
                break;
        }
    }
    
    public static long GetServerTimeMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public IEnumerable<IConnector> GetAllConnectors() => _connectors;
    public IEnumerable<IConnector> GetConnectors(string name) => _connectors.Where(c => c.Name == name); 
    public IConnector GetConnector(string name, string host, int port) => _connectors.FirstOrDefault(x => x.Name == name && x.Host == host && x.Port == port);
}
