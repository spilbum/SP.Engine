using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SP.Core;
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
    bool Initialize(string name, EngineConfig config);
    bool Start();
    void Stop();
}

public abstract class Engine : BaseEngine, IEngine
{
    private readonly Dictionary<ushort, ICommand> _userCommands = new();
    private readonly Dictionary<ushort, ICommand> _internalCommands = new();
    private readonly List<IConnector> _connectors = [];
    private readonly List<PeerFiber> _peerFibers = [];
    private int _nextFiberIndex = -1;
    private PeerManager _peerManager;
    private PerfMonitor _perfMonitor;
    private ILogger _perfLogger;
    private Timer _waitingReconnectCheckingTimer;

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
        Scheduler.Schedule(ScheduleUpdateConnectors, connectorUpdatePeriod, connectorUpdatePeriod);

        var fiberCnt = Math.Max(Config.Network.LimitConnectionCount / 300, 10);
        var period = TimeSpan.FromMilliseconds(Config.Session.PeerUpdateIntervalMs);
        
        for (var index = 0; index < fiberCnt; index++)
        {
            var fiber = new PeerFiber(Logger, $"PeerFiber-{index:D2}");
            _peerFibers.Add(fiber);
            Scheduler.Schedule(() => { fiber.Enqueue(fiber.Tick); }, period, period);
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
    
    internal bool OnlinePeer(BasePeer peer, ISession session)
        => _peerManager.Online(peer, session);

    internal void JoinPeer(BasePeer peer)
        => _peerManager.Join(peer);
    
    internal bool CreatePeer(ISession session, out BasePeer peer)
    {
        peer = CreatePeer(session) as BasePeer;
        return peer != null;
    }
    
    protected abstract IPeer CreatePeer(ISession session);
    
    protected override void OnNewSessionConnected(Session session)
    {
        base.OnNewSessionConnected(session);

        var current = Interlocked.Increment(ref _nextFiberIndex);
        var index = (int)((uint)current % _peerFibers.Count);
        var fiber = _peerFibers[index];
        
        session.Fiber = fiber;
        fiber.AddSession(session);
    }

    protected override void OnSessionClosed(Session session, CloseReason reason)
    {
        base.OnSessionClosed(session, reason);

        if (session.Fiber is PeerFiber fiber)
            fiber.RemoveSession(session);
        
        var peer = session.Peer;
        if (null == peer) return;
        
        if (session.IsClosing)
        {
            // 종료중이면 제거
            _peerManager.RemovePeer(peer, reason);
            return;
        }
        
        if (peer.State is PeerState.Authenticated or PeerState.Online)
        {
            // 연결 끊김
            _peerManager.Offline(peer, reason);
        }
        else
        {
            _peerManager.RemovePeer(peer, reason);
        }
    }
    
    private void StartReconnectTimer()
    {
        var ts = TimeSpan.FromSeconds(Config.Session.WaitingReconnectTimerIntervalSec);
        _waitingReconnectCheckingTimer = new Timer(OnCheckWaitingReconnectCallback, null, ts, ts);
    }

    private void StopReconnectTimer()
    {
        _waitingReconnectCheckingTimer?.Dispose();
        _waitingReconnectCheckingTimer = null;
    }

    private void OnCheckWaitingReconnectCallback(object state)
    {
        if (null == _waitingReconnectCheckingTimer) return;
        _waitingReconnectCheckingTimer.Change(Timeout.Infinite, Timeout.Infinite);

        try
        {
            _peerManager.CheckTimeouts();
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error checking reconnect timeouts");
        }
        finally
        {
            if (_waitingReconnectCheckingTimer != null)
            {
                var ts = TimeSpan.FromSeconds(Config.Session.WaitingReconnectTimerIntervalSec);
                _waitingReconnectCheckingTimer.Change(ts, ts);
            }
        }
    }
    
    private void SetupPerfMonitor(PerfConfig config)
    {
        if (!config.MonitorEnabled)
            return;

        _perfMonitor = new PerfMonitor();
        Scheduler.Schedule(_perfMonitor.Tick, TimeSpan.Zero, config.SamplePeriod);

        if (!config.LoggerEnabled)
            return;

        _perfLogger = LogManager.GetLogger("PerfMonitor");
        Scheduler.Schedule(() =>
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
                var connector = CreateConnector(config.Name);
                if (connector == null)
                {
                    Logger.Info("Failed to create connector. name={0}.", config.Name);
                    return false;
                }
                
                if (!connector.Initialize(Logger, Scheduler, config))
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

    internal async Task ExecuteMessageAsync(Session session, IMessage message)
    {
        var command = GetInternalCommand(message.Id);
        if (command != null)
        {
            await command.Execute(session, message);
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
                    if (cmd != null) await cmd.Execute(peer, msg);
                }
                break;
            case UdpMessage udp:
                await command.Execute(peer, udp);
                break;
        }
    }
    
    public static long GetServerTimeMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public IEnumerable<IConnector> GetAllConnectors() => _connectors;
    public IEnumerable<IConnector> GetConnectors(string name) => _connectors.Where(c => c.Name == name); 
    public IConnector GetConnector(string name, string host, int port) => _connectors.FirstOrDefault(x => x.Name == name && x.Host == host && x.Port == port);
}
