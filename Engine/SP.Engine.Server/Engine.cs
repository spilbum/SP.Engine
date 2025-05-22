using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using SP.Common.Fiber;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Handler;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.Connector;
using SP.Engine.Protocol;
using SP.Engine.Runtime.Message;
using SP.Engine.Server.ProtocolHandler;

namespace SP.Engine.Server
{
    public abstract class Engine<TPeer> : BaseEngine<Session<TPeer>>
        where TPeer : BasePeer, IPeer
    {
        private sealed class WaitingReconnectPeer(TPeer peer, int timeOutSec)
        {
            public TPeer Peer { get; } = peer;
            public DateTime ExpireTime { get; } = DateTime.UtcNow.AddSeconds(timeOutSec);
        }

        private readonly ConcurrentDictionary<EPeerId, WaitingReconnectPeer> _waitingReconnectPeerDict = new();
        private readonly ConcurrentDictionary<EPeerId, TPeer> _peerDict = new();
        private readonly List<IServerConnector> _connectors = [];
        private readonly List<ThreadFiber> _updatePeerFibers = [];
        private readonly Dictionary<EProtocolId, IHandler<Session<TPeer>, IMessage>> _engineHandlerDict = new();
        private readonly Dictionary<EProtocolId, IHandler<TPeer, IMessage>> _handlerDict = new();
        private ThreadFiber _fiber;
        
        public IFiberScheduler Scheduler => _fiber;

        public override bool Initialize(string name, IEngineConfig config)
        {
            if (!base.Initialize(name, config))
                return false;

            // 스케줄러 생성
            _fiber = CreateFiber();
            _fiber.Schedule(ScheduleUpdateConnectors, 16, 16);
            
            // 파이버 하나당 300명 계산. 최대 8개 생성함
            var fiberCnt = Math.Max(config.LimitConnectionCount / 300, 8);
            for (var index = 0; index < fiberCnt; index++)
            {
                var fiber = CreateFiber();
                fiber.Schedule(ScheduleUpdatePeers, index, 16, 16);
                _updatePeerFibers.Add(fiber);
            }
            
            if (!SetupHandler())
                return false;

            if (!SetupConnector(config))
                return false;
            
            Logger.Info("The server {0} is initialized.", name);
            return true;
        }
        
        private ThreadFiber CreateFiber()
        {
            var fiber = new ThreadFiber(logger: Logger);
            return fiber;
        }

        private bool SetupConnector(IEngineConfig config)
        {
            foreach (var connectorConfig in config.Connectors)
            {
                var connector = CreateConnector(connectorConfig.Name);
                if (null == connector)
                {
                    Logger.Info("Failed to create connector {0}.", connectorConfig.Name);
                    return false;
                }

                try
                {
                    if (!connector.Initialize(this, connectorConfig))
                        throw new InvalidOperationException($"Failed to initialize connector {connector.Name}.");

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
                _engineHandlerDict[EngineProtocol.C2S.SessionAuthReq] = new SessionAuth<TPeer>();
                _engineHandlerDict[EngineProtocol.C2S.Close] = new Close<TPeer>();
                _engineHandlerDict[EngineProtocol.C2S.Ping] = new Ping<TPeer>();
                _engineHandlerDict[EngineProtocol.C2S.MessageAck] = new MessageAck<TPeer>();
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
                
                if (Activator.CreateInstance(type) is IHandler<TPeer, IMessage> handler)
                    yield return handler;
            }
        }

        private bool RegisterHandler(IHandler<TPeer, IMessage> handler)
        {
            if (_handlerDict.TryAdd(handler.Id, handler)) return true;
            Logger.Error("Handler '{0}' already exists.", handler.Id);
            return false;
        }

        private IHandler<TPeer, IMessage> GetHandler(EProtocolId protocolId)
        {
            _handlerDict.TryGetValue(protocolId, out var handler);
            return handler;
        }
        
        internal void ExecuteMessage(Session<TPeer> session, IMessage message)
        {
            var handler = GetHandler(message.ProtocolId);
            if (handler != null)
            {
                var peer = session.Peer;
                if (peer == null)
                {
                    Logger.Error("Peer is null.");
                    session.Close();
                    return;
                }
                
                session.SendMessageAck(message.SequenceNumber);
                foreach (var pendingMessage in peer.DrainInOrderReceivedMessages(message))
                {
                    GetHandler(pendingMessage.ProtocolId).ExecuteMessage(peer, pendingMessage);
                }
            }
            else
            {
                if (_engineHandlerDict.TryGetValue(message.ProtocolId, out var engineHandler))
                    engineHandler.ExecuteMessage(session, message);
                else
                {
                    Logger.Error("Unknown message: {0} Session: {1}/{2}", message.ProtocolId, session.SessionId, session.RemoteEndPoint);
                    session.Close();
                }
            }
        }
        
        public override bool Start()
        {
            if (!base.Start())
                return false;

            _fiber?.Start();
            
            foreach (var fiber in _updatePeerFibers)
                fiber.Start();

            foreach (var connector in _connectors)
                connector.Connect();
            
            StartWaitingReconnectPeerCheckingTimer();

            return true;
        }

        public override void Stop()
        {
            _fiber?.Dispose();
            
            foreach (var fiber in _updatePeerFibers)
                fiber.Dispose();
            _updatePeerFibers.Clear();
            
            foreach (var connector in _connectors)
                connector.Close();
            
            StopWaitingReconnectPeerCheckingTimer();
            
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

        private Timer _waitingReconnectPeerCheckingTimer;

        private void StartWaitingReconnectPeerCheckingTimer()
        {
            _waitingReconnectPeerCheckingTimer = 
                new Timer(OnCheckWaitingReconnectPeerCallback, null, TimeSpan.FromSeconds(Config.WaitingReconnectPeerTimerIntervalSec), TimeSpan.FromSeconds(Config.WaitingReconnectPeerTimerIntervalSec));
        }

        private void StopWaitingReconnectPeerCheckingTimer()
        {
            _waitingReconnectPeerCheckingTimer?.Dispose();
            _waitingReconnectPeerCheckingTimer = null;
        }
        
        private void OnCheckWaitingReconnectPeerCallback(object state)
        {
            if (null == _waitingReconnectPeerCheckingTimer)
                return;
            
            _waitingReconnectPeerCheckingTimer.Change(Timeout.Infinite, Timeout.Infinite);

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
                    peer.LeaveServer(ECloseReason.TimeOut);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
            finally
            {
                _waitingReconnectPeerCheckingTimer.Change(TimeSpan.FromSeconds(Config.WaitingReconnectPeerTimerIntervalSec), TimeSpan.FromSeconds(Config.WaitingReconnectPeerTimerIntervalSec));
            }
        }

        private void ScheduleUpdatePeers(int index)
        {
            var sessions = SessionsSource;
            var peers = sessions
                .Where(x => x.Value.Peer != null && (int)x.Value.Peer.PeerId % _updatePeerFibers.Count == index)
                .Select(x => x.Value.Peer);
            foreach (var peer in peers)
                peer.Update();
        }

        public abstract TPeer CreatePeer(ISession<TPeer> session, DhKeySize dhKeySize, byte[] dhPublicKey);
        protected abstract IServerConnector CreateConnector(string name);

        public TPeer FindPeer(EPeerId peerId)
        {
            if (!_peerDict.TryGetValue(peerId, out var peer))
                peer = GetWaitingReconnectPeer(peerId);
            return peer;
        }

        protected virtual bool AddOrUpdatePeer(TPeer peer)
        {
            switch (peer.PeerType)
            {
                case EPeerType.User:
                    return _peerDict.TryAdd(peer.PeerId, peer);
                case EPeerType.Server:
                    _peerDict.AddOrUpdate(peer.PeerId, peer, (_, _) => peer);
                    return true;
                case EPeerType.None:
                default:
                    Logger.Error("Invalid peer: {0}", peer.PeerType);
                    return false;
            }
        }

        protected virtual bool TryRemovePeer(EPeerId peerId, out TPeer peer)
        {
            if (_peerDict.TryRemove(peerId, out var removed))
            {
                peer = removed;
                return true;
            }

            peer = null;
            return false;
        }

        protected override void OnSessionClosed(Session<TPeer> session, ECloseReason reason)
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
            
            if (peer.State == EPeerState.Authorized || peer.State == EPeerState.Online)
                OfflinePeer(peer, reason);
            else
                LeavePeer(peer, reason);
        }

        private void OfflinePeer(TPeer peer, ECloseReason reason)
        {
            RegisterWaitingReconnectPeer(peer);
            peer.Offline(reason);
        }

        internal void OnlinePeer(TPeer peer, ISession<TPeer> session)
        {
            RemoveWaitingReconnectPeer(peer);
            AddOrUpdatePeer(peer);
            peer.Online(session);
        }

        internal void JoinPeer(TPeer peer)
        {
            if (!_peerDict.TryAdd(peer.PeerId, peer))
                return;

            peer.JoinServer();
        }

        private void LeavePeer(TPeer peer, ECloseReason reason)
        {
            if (!TryRemovePeer(peer.PeerId, out var removed))
            {
                Logger.Error("Failed to remove peer: {0}", peer);
                return;
            }

            Logger.Debug("Peer removed. peerId={0}", removed.PeerId);
            removed.LeaveServer(reason);
        }

        private void RegisterWaitingReconnectPeer(TPeer peer)
        {
            if (!TryRemovePeer(peer.PeerId, out _))
                return;

            if (!_waitingReconnectPeerDict.TryAdd(peer.PeerId, new WaitingReconnectPeer(peer, Config.WaitingReconnectPeerTimeOutSec)))
                return;
            
            Logger.Debug("Peer waiting reconnect registered. peerId={0}", peer.PeerId);
        }

        private void RemoveWaitingReconnectPeer(TPeer peer)
        {
            if (!_waitingReconnectPeerDict.TryRemove(peer.PeerId, out _))
                return;
            
            Logger.Debug("Peer waiting reconnect removed. peerId={0}", peer.PeerId);
        }

        public TPeer GetWaitingReconnectPeer(EPeerId peerId)
        {
            _waitingReconnectPeerDict.TryGetValue(peerId, out var waitingReconnectPeer);
            return waitingReconnectPeer?.Peer;
        }
    }
}
