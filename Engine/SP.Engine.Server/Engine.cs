using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
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
    bool Initialize(string name, EngineConfig config);
    bool Start();
    void Stop();
}

public abstract class Engine : BaseEngine, IEngine
{
    private readonly Dictionary<ushort, ICommand> _commands = new();
    private readonly List<IConnector> _connectors = [];
    private readonly Dictionary<ushort, ICommand> _internalCommands = new();
    private readonly ConcurrentDictionary<uint, BasePeer> _peers = new();
    private readonly List<ThreadFiber> _updatePeerFibers = [];

    private readonly ConcurrentDictionary<uint, WaitingReconnectPeer> _waitingReconnectPeerDict = new();
    private ILogger _perfLogger;
    private PerfMonitor _perfMonitor;

    private Timer _waitingReconnectCheckingTimer;

    public override bool Initialize(string name, EngineConfig config)
    {
        if (!base.Initialize(name, config))
            return false;

        var connectorUpdatePeriod = TimeSpan.FromMilliseconds(config.Session.ConnectorUpdateIntervalMs);
        Scheduler.Schedule(ScheduleUpdateConnectors, connectorUpdatePeriod, connectorUpdatePeriod);

        var fiberCnt = Math.Max(config.Network.LimitConnectionCount / 300, 10);
        for (var index = 0; index < fiberCnt; index++)
        {
            var fiber = new ThreadFiber(Logger, $"PeerFiber-{index:D2}");
            var peerUpdatePeriod = TimeSpan.FromMilliseconds(config.Session.PeerUpdateIntervalMs);
            Scheduler.Schedule(ScheduleUpdatePeers, index, peerUpdatePeriod, peerUpdatePeriod);
            _updatePeerFibers.Add(fiber);
        }

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

        foreach (var connector in _connectors) connector.Connect();
        StartWaitingReconnectCheckingTimer();

        return true;
    }

    public override void Stop()
    {
        _perfMonitor?.Dispose();
        foreach (var fiber in _updatePeerFibers) fiber.Dispose();
        foreach (var connector in _connectors) connector.Close();
        StopWaitingReconnectCheckingTimer();

        base.Stop();
    }

    protected override BasePeer GetBasePeer(uint peerId)
    {
        return FindPeerByPeerId<BasePeer>(peerId);
    }

    public static long GetServerTimeMs()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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
            if (!TryCreateConnector(config.Name, out var connector))
            {
                Logger.Info("Failed to create connector. name={0}.", config.Name);
                return false;
            }

            try
            {
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

    private bool SetupCommand()
    {
        try
        {
            _internalCommands[C2SEngineProtocolId.SessionAuthReq] = new SessionAuth();
            _internalCommands[C2SEngineProtocolId.Close] = new Close();
            _internalCommands[C2SEngineProtocolId.Ping] = new Ping();
            _internalCommands[C2SEngineProtocolId.MessageAck] = new MessageAck();
            _internalCommands[C2SEngineProtocolId.UdpHelloReq] = new UdpHelloReq();
            _internalCommands[C2SEngineProtocolId.UdpKeepAlive] = new UdpKeepAlive();

            DiscoverCommands();
            Logger.Debug("Discover commands: [{0}]", string.Join(", ", _commands.Keys));
            return true;
        }
        catch (Exception e)
        {
            Logger.Error(e);
            return false;
        }
    }

    private void DiscoverCommands()
    {
        var assembly = GetType().Assembly;
        var types = assembly.GetTypes();
        var peerTypes = types.Where(t => typeof(IPeer).IsAssignableFrom(t)).ToList();

        foreach (var t in types)
        {
            if (!t.IsClass || t.IsAbstract) continue;
            var attr = t.GetCustomAttribute<ProtocolCommandAttribute>();
            if (attr == null) continue;
            if (!typeof(ICommand).IsAssignableFrom(t)) continue;
            if (Activator.CreateInstance(t) is not ICommand command) continue;
            if (!peerTypes.Contains(command.ContextType)) continue;
            if (!_commands.TryAdd(attr.Id, command))
                throw new Exception($"Duplicate command: {attr.Id}");
        }
    }

    private ICommand GetCommand(ushort id)
    {
        _commands.TryGetValue(id, out var command);
        return command;
    }

    internal void ExecuteMessage(Session session, IMessage message)
    {
        if (_internalCommands.TryGetValue(message.Id, out var command))
        {
            command.Execute(session, message);
        }
        else
        {
            command = GetCommand(message.Id);
            if (command != null)
            {
                var peer = session.Peer;
                if (peer == null)
                {
                    Logger.Warn("Not found peer. sessionId={0}", session.Id);
                    session.Close();
                    return;
                }

                switch (message)
                {
                    case TcpMessage tcp:
                        session.SendMessageAck(tcp.SequenceNumber);
                        foreach (var inOrder in peer.ProcessMessageInOrder(message))
                        {
                            command = GetCommand(inOrder.Id);
                            command?.Execute(peer, inOrder);
                        }

                        break;
                    case UdpMessage udp:
                        command = GetCommand(udp.Id);
                        command?.Execute(peer, udp);
                        break;
                }
            }
            else
            {
                if (message is TcpMessage tcp)
                    session.SendMessageAck(tcp.SequenceNumber);

                Logger.Error("Unknown command: msgId={0}, session={1}/{2}", message.Id, session.Id,
                    session.RemoteEndPoint);
            }
        }
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

    public IEnumerable<IConnector> GetAllConnectors()
    {
        return _connectors;
    }

    public IEnumerable<IConnector> GetConnectors(string name)
    {
        return _connectors.Where(connector => connector.Name == name);
    }

    public IConnector GetConnector(string name, string host, int port)
    {
        return _connectors.FirstOrDefault(x => x.Name == name && x.Host == host && x.Port == port);
    }

    private void StartWaitingReconnectCheckingTimer()
    {
        var ts = TimeSpan.FromSeconds(Config.Session.WaitingReconnectTimerIntervalSec);
        _waitingReconnectCheckingTimer = new Timer(OnCheckWaitingReconnectCallback, null, ts, ts);
    }

    private void StopWaitingReconnectCheckingTimer()
    {
        _waitingReconnectCheckingTimer?.Dispose();
        _waitingReconnectCheckingTimer = null;
    }

    private void OnCheckWaitingReconnectCallback(object state)
    {
        if (null == _waitingReconnectCheckingTimer)
            return;

        _waitingReconnectCheckingTimer.Change(Timeout.Infinite, Timeout.Infinite);

        try
        {
            foreach (var waitingReconnectPeer in _waitingReconnectPeerDict.Values)
            {
                var peer = waitingReconnectPeer.Peer;
                var expireTime = waitingReconnectPeer.ExpireTime;

                if (DateTime.UtcNow < expireTime)
                    continue;

                Logger.Debug("Client terminated due to timeout. peerId={0}", peer.PeerId);

                // 재 연결 타임아웃으로 종료함
                _waitingReconnectPeerDict.TryRemove(peer.PeerId, out _);
                peer.LeaveServer(CloseReason.TimeOut);
            }
        }
        catch (Exception e)
        {
            Logger.Error(e);
        }
        finally
        {
            var ts = TimeSpan.FromSeconds(Config.Session.WaitingReconnectTimerIntervalSec);
            _waitingReconnectCheckingTimer.Change(ts, ts);
        }
    }

    private void ScheduleUpdatePeers(int index)
    {
        var source = SessionsSource;
        foreach (var kvp in source)
        {
            if (kvp.Value is not Session s || s.Peer == null)
                continue;

            if (s.Peer.PeerId % _updatePeerFibers.Count != index)
                continue;

            s.Peer.Tick();
        }
    }

    internal bool CreatePeer(ISession session, out IPeer peer)
    {
        return TryCreatePeer(session, out peer);
    }

    protected abstract bool TryCreatePeer(ISession session, out IPeer peer);
    protected abstract bool TryCreateConnector(string name, out IConnector connector);

    protected TPeer FindPeerByPeerId<TPeer>(uint peerId)
        where TPeer : BasePeer
    {
        if (!_peers.TryGetValue(peerId, out var peer))
            peer = GetWaitingReconnectPeer(peerId);
        return peer as TPeer;
    }

    protected virtual bool AddOrUpdatePeer(BasePeer peer)
    {
        switch (peer.Kind)
        {
            case PeerKind.User:
                return _peers.TryAdd(peer.PeerId, peer);
            case PeerKind.Server:
                _peers.AddOrUpdate(peer.PeerId, peer,
                    (_, exists) =>
                    {
                        ((Session)exists.Session).UpdatePeer(peer);
                        return peer;
                    });
                return true;
            case PeerKind.None:
            default:
                Logger.Error("Unknown peer kind: {0}", peer.Kind);
                return false;
        }
    }

    protected virtual bool TryRemovePeer(uint peerId, out BasePeer peer)
    {
        if (_peers.TryRemove(peerId, out var removed))
        {
            peer = removed;
            return true;
        }

        peer = null;
        return false;
    }

    internal override void OnSessionClosed(Session session, CloseReason reason)
    {
        base.OnSessionClosed(session, reason);

        var peer = session.Peer;
        if (null == peer)
            return;

        if (session.IsClosing)
        {
            LeavePeer(peer, reason);
            return;
        }

        if (peer.State is PeerState.Authenticated or PeerState.Online) OfflinePeer(peer, reason);
        else LeavePeer(peer, reason);
    }

    private void OfflinePeer(BasePeer peer, CloseReason reason)
    {
        RegisterWaitingReconnectPeer(peer);
        peer.Offline(reason);
    }

    internal void OnlinePeer(BasePeer peer, ISession session)
    {
        RemoveWaitingReconnectPeer(peer);
        if (AddOrUpdatePeer(peer))
            peer.Online(session);
        else
            Logger.Warn("Failed to add or update peer. peerId={0}", peer.PeerId);
    }

    internal void JoinPeer(BasePeer peer)
    {
        if (!_peers.TryAdd(peer.PeerId, peer))
            return;

        peer.JoinServer();
    }

    private void LeavePeer(BasePeer peer, CloseReason reason)
    {
        if (!TryRemovePeer(peer.PeerId, out var removed))
        {
            Logger.Error("Failed to remove peer: {0}", peer);
            return;
        }

        removed.LeaveServer(reason);
        OnPeerLeaved(removed, reason);
    }

    protected virtual void OnPeerLeaved(BasePeer peer, CloseReason reason)
    {
    }

    private void RegisterWaitingReconnectPeer(BasePeer peer)
    {
        if (!TryRemovePeer(peer.PeerId, out _))
            return;

        if (!_waitingReconnectPeerDict.TryAdd(peer.PeerId,
                new WaitingReconnectPeer(peer, Config.Session.WaitingReconnectTimeoutSec)))
            return;

        Logger.Debug("Peer waiting reconnect registered. peerId={0}", peer.PeerId);
    }

    private void RemoveWaitingReconnectPeer(BasePeer peer)
    {
        if (!_waitingReconnectPeerDict.TryRemove(peer.PeerId, out _))
            return;

        Logger.Debug("Peer waiting reconnect removed. peerId={0}", peer.PeerId);
    }

    public BasePeer GetWaitingReconnectPeer(uint peerId)
    {
        _waitingReconnectPeerDict.TryGetValue(peerId, out var waitingReconnectPeer);
        return waitingReconnectPeer?.Peer;
    }

    private sealed class WaitingReconnectPeer(BasePeer peer, int timeOutSec)
    {
        public BasePeer Peer { get; } = peer;
        public DateTime ExpireTime { get; } = DateTime.UtcNow.AddSeconds(timeOutSec);
    }
}
