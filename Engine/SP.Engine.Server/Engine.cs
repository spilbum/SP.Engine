using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using SP.Common.Fiber;
using SP.Common.Logging;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Handler;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.Connector;
using SP.Engine.Protocol;
using SP.Engine.Runtime.Networking;
using SP.Engine.Server.ProtocolHandler;

namespace SP.Engine.Server
{
    public interface IEngine : ILogContext
    {
        IEngineConfig Config { get; }
        IFiberScheduler Scheduler { get; }
        bool Initialize(string name, EngineConfig config);
        bool Start();
        void Stop();
        IPeer GetPeer(uint peerId);
    }
    
    public abstract class Engine<TPeer> : BaseEngine<Session<TPeer>>, IEngine
        where TPeer : BasePeer, IPeer
    {
        private sealed class WaitingReconnectPeer(TPeer peer, int timeOutSec)
        {
            public TPeer Peer { get; } = peer;
            public DateTime ExpireTime { get; } = DateTime.UtcNow.AddSeconds(timeOutSec);
        }

        private readonly ConcurrentDictionary<uint, WaitingReconnectPeer> _waitingReconnectPeerDict = new();
        private readonly ConcurrentDictionary<uint, TPeer> _peers = new();
        private readonly List<IServerConnector> _connectors = [];
        private readonly List<ThreadFiber> _updatePeerFibers = [];
        private readonly Dictionary<ushort, IHandler<Session<TPeer>, IMessage>> _engineHandlers = new();
        private readonly Dictionary<ushort, IHandler<TPeer, IMessage>> _handlers = new();
        private ThreadFiber _scheduler;

        public override IFiberScheduler Scheduler => _scheduler;
        
        protected override IBasePeer GetBasePeer(uint peerId)
            => FindPeer(peerId);
        
        public IPeer GetPeer(uint peerId)
            => FindPeer(peerId);
        
        public override bool Initialize(string name, EngineConfig config)
        {
            if (!base.Initialize(name, config))
                return false;

            _scheduler = new ThreadFiber(Logger);
            _scheduler.Schedule(ScheduleUpdateConnectors, config.Session.ConnectorUpdateIntervalMs, config.Session.ConnectorUpdateIntervalMs);
            
            var fiberCnt = Math.Max(config.Network.LimitConnectionCount / 300, 10);
            for (var index = 0; index < fiberCnt; index++)
            {
                var fiber = new ThreadFiber(Logger);
                fiber.Schedule(ScheduleUpdatePeers, index, config.Session.PeerUpdateIntervalMs, config.Session.PeerUpdateIntervalMs);
                _updatePeerFibers.Add(fiber);
            }
            
            if (!SetupHandler())
                return false;

            if (!SetupConnector(config.Connectors))
                return false;

            Logger.Info("The server {0} is initialized.", name);
            return true;
        }
        
        public long GetServerTimeMs()
            => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
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
                    if (!connector.Initialize(this, config))
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
        
        private bool SetupHandler()
        {
            try
            {
                _engineHandlers[C2SEngineProtocolId.SessionAuthReq] = new SessionAuth<TPeer>();
                _engineHandlers[C2SEngineProtocolId.Close] = new Close<TPeer>();
                _engineHandlers[C2SEngineProtocolId.Ping] = new Ping<TPeer>();
                _engineHandlers[C2SEngineProtocolId.MessageAck] = new MessageAck<TPeer>();
                _engineHandlers[C2SEngineProtocolId.UdpHelloReq] = new UdpHelloReq<TPeer>();
                _engineHandlers[C2SEngineProtocolId.UdpKeepAlive] = new UdpKeepAlive<TPeer>();
                return DiscoverHandlers().All(RegisterHandler);
            }
            catch (Exception e)
            {
                Logger.Error(e);
                return false;
            }
        }

        private IEnumerable<IHandler<TPeer, IMessage>> DiscoverHandlers()
        {
            var assembly = GetType().Assembly;
            foreach (var type in assembly.GetTypes())
            {
                if (!type.IsClass || type.IsAbstract)
                    continue;

                var attr = type.GetCustomAttribute<ProtocolHandlerAttribute>();
                if (attr == null)
                    continue;

                if (!typeof(IHandler).IsAssignableFrom(type))
                    continue;
                
                if (Activator.CreateInstance(type) is not IHandler<TPeer, IMessage> handler)
                    continue;
                
                yield return handler;
            }
        }

        private bool RegisterHandler(IHandler<TPeer, IMessage> handler)
        {
            if (!_handlers.TryAdd(handler.Id, handler))
            {
                Logger.Error("Handler '{0}' already exists.", handler.Id);
                return false;
            }

            Logger.Debug("Handler '{0}' registered.", handler.Id);
            return true;
        }

        private IHandler<TPeer, IMessage> GetHandler(ushort id)
        {
            _handlers.TryGetValue(id, out var handler);
            return handler;
        }
        
        internal void ExecuteMessage(Session<TPeer> session, IMessage message)
        {
            var handler = GetHandler(message.Id);
            if (handler != null)
            {
                var peer = session.Peer;
                if (peer == null)
                {
                    Logger.Warn("Not found peer. sessionId={0}", session.Id);
                    session.Close();
                    return;
                }
                
                if (message is TcpMessage tcp)
                    session.SendMessageAck(tcp.SequenceNumber);

                foreach (var msg in peer.ProcessMessageInOrder(message))
                    GetHandler(message.Id)?.ExecuteMessage(peer, msg); 
            }
            else
            {
                if (_engineHandlers.TryGetValue(message.Id, out var value))
                    value.ExecuteMessage(session, message);
                else
                    Logger.Error("Unknown message: {0} Session: {1}/{2}", message.Id, session.Id, session.RemoteEndPoint);
            }
        }

        public override bool Start()
        {
            if (!base.Start())
                return false;

            _scheduler?.Start();
            
            foreach (var fiber in _updatePeerFibers)
                fiber.Start();

            foreach (var connector in _connectors)
                connector.Connect();
            
            StartWaitingReconnectCheckingTimer();

            return true;
        }

        public override void Stop()
        {
            _scheduler?.Dispose();
            
            foreach (var fiber in _updatePeerFibers)
                fiber.Dispose();
            _updatePeerFibers.Clear();
            
            foreach (var connector in _connectors)
                connector.Close();
            
            StopWaitingReconnectCheckingTimer();
            
            base.Stop();
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

        public IEnumerable<IServerConnector> GetAllConnectors()
        {
            return _connectors;
        }
        
        public IEnumerable<IServerConnector> GetConnectors(string name)
        {
            return _connectors.Where(connector => connector.Name == name);
        }
        
        public IServerConnector GetConnector(string name, string host, int port)
        {
            return _connectors.FirstOrDefault(x => x.Name == name && x.Host == host && x.Port == port);
        }

        private Timer _waitingReconnectCheckingTimer;

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
                    
                    Logger.Debug("Client terminated due to timeout. peerId={0}", peer.Id);
                 
                    // 재 연결 타임아웃으로 종료함
                    _waitingReconnectPeerDict.TryRemove(peer.Id, out _);
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
            var sessions = SessionsSource;
            var peers = sessions
                .Where(x => x.Value.Peer != null && (int)x.Value.Peer.Id % _updatePeerFibers.Count == index)
                .Select(x => x.Value.Peer);
            foreach (var peer in peers)
                peer.Tick();
        }

        internal TPeer CreatePeer(ISession session)
            => TryCreatePeer(session, out var peer) ? peer : null;
        
        protected abstract bool TryCreatePeer(ISession session, out TPeer peer);
        protected abstract bool TryCreateConnector(string name, out IServerConnector connector);

        public TPeer FindPeer(uint peerId)
        {
            if (!_peers.TryGetValue(peerId, out var peer))
                peer = GetWaitingReconnectPeer(peerId);
            return peer;
        }

        protected virtual bool AddOrUpdatePeer(TPeer peer)
        {
            switch (peer.Kind)
            {
                case PeerKind.User:
                    return _peers.TryAdd(peer.Id, peer);
                case PeerKind.Server:
                    _peers.AddOrUpdate(peer.Id, peer, (_, _) => peer);
                    return true;
                case PeerKind.None:
                default:
                    Logger.Error("Unknown peer kind: {0}", peer.Kind);
                    return false;
            }
        }

        protected virtual bool TryRemovePeer(uint peerId, out TPeer peer)
        {
            if (_peers.TryRemove(peerId, out var removed))
            {
                peer = removed;
                return true;
            }

            peer = null;
            return false;
        }

        protected override void OnSessionClosed(Session<TPeer> clientSession, CloseReason reason)
        {
            base.OnSessionClosed(clientSession, reason);

            var peer = clientSession.Peer;
            if (null == peer)
                return;

            if (clientSession.IsClosing)
            {
                LeavePeer(peer, reason);
                return;
            }
            
            if (peer.State == PeerState.Authorized || peer.State == PeerState.Online)
                OfflinePeer(peer, reason);
            else
                LeavePeer(peer, reason);
        }

        private void OfflinePeer(TPeer peer, CloseReason reason)
        {
            RegisterWaitingReconnectPeer(peer);
            peer.Offline(reason);
        }

        internal void OnlinePeer(TPeer peer, ISession session)
        {
            RemoveWaitingReconnectPeer(peer);
            if (AddOrUpdatePeer(peer)) 
                peer.Online(session);
            else
                Logger.Warn("Failed to add or update peer. peerId={0}", peer.Id);
        }

        internal void JoinPeer(TPeer peer)
        {
            if (!_peers.TryAdd(peer.Id, peer))
                return;

            peer.JoinServer();
        }

        private void LeavePeer(TPeer peer, CloseReason reason)
        {
            if (!TryRemovePeer(peer.Id, out var removed))
            {
                Logger.Error("Failed to remove peer: {0}", peer);
                return;
            }

            Logger.Debug("Peer removed. peerId={0}", removed.Id);
            removed.LeaveServer(reason);
        }

        internal void UpdatePolicy()
        {
            
        }

        private void RegisterWaitingReconnectPeer(TPeer peer)
        {
            if (!TryRemovePeer(peer.Id, out _))
                return;

            if (!_waitingReconnectPeerDict.TryAdd(peer.Id, new WaitingReconnectPeer(peer, Config.Session.WaitingReconnectTimeoutSec)))
                return;
            
            Logger.Debug("Peer waiting reconnect registered. peerId={0}", peer.Id);
        }

        private void RemoveWaitingReconnectPeer(TPeer peer)
        {
            if (!_waitingReconnectPeerDict.TryRemove(peer.Id, out _))
                return;
            
            Logger.Debug("Peer waiting reconnect removed. peerId={0}", peer.Id);
        }

        public TPeer GetWaitingReconnectPeer(uint peerId)
        {
            _waitingReconnectPeerDict.TryGetValue(peerId, out var waitingReconnectPeer);
            return waitingReconnectPeer?.Peer;
        }
    }
}
