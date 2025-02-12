using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using SP.Engine.Common;
using SP.Engine.Common.Fiber;
using SP.Engine.Common.Logging;
using SP.Engine.Core;
using SP.Engine.Core.Protocol;
using SP.Engine.Core.Utility;
using SP.Engine.Server.Connector;

namespace SP.Engine.Server
{
    public interface IServer
    {
        ILogger Logger { get; }
        IServerConfig Config { get; }
    }

    public abstract class ServerBase<TPeer> : SessionServerBase<ClientSession<TPeer>>, IServer
        where TPeer : PeerBase, IPeer
    {
        private sealed class WaitingReconnectPeer(TPeer peer, int timeOutSec)
        {
            public TPeer Peer { get; } = peer;
            public DateTime ExpireTime { get; } = DateTime.UtcNow.AddSeconds(timeOutSec);
        }

        private readonly ConcurrentDictionary<EPeerId, WaitingReconnectPeer> _waitingReconnectPeerDict = new ConcurrentDictionary<EPeerId, WaitingReconnectPeer>();
        private readonly ConcurrentDictionary<EPeerId, TPeer> _peerDict = new ConcurrentDictionary<EPeerId, TPeer>();
        private readonly List<IConnector> _connectors = new List<IConnector>();
        private readonly List<ThreadFiber> _updatePeerFibers = new List<ThreadFiber>();
        private ThreadFiber _fiber;
        
        public IFiberScheduler Scheduler => _fiber;

        public override bool Initialize(string name, IServerConfig config)
        {
            if (!base.Initialize(name, config))
                return false;

            // 스케줄러 생성
            _fiber = CreateFiber();
            _fiber.Schedule(ScheduleUpdateConnectors, 50, 50);
            
            // 파이버 하나당 300명 계산. 최대 8개 생성함
            var fiberCnt = Math.Max(config.LimitConnectionCount / 300, 8);
            for (var index = 0; index < fiberCnt; index++)
            {
                var fiber = CreateFiber();
                fiber.Schedule(ScheduleUpdatePeers, index, 50, 50);
                _updatePeerFibers.Add(fiber);
            }
            
            if (!SetupProtocolLoader())
                return false;

            if (!SetupConnector(config))
                return false;
            
            Logger.WriteLog(ELogLevel.Info, "The server {0} is initialized.", name);
            return true;
        }
        
        private ThreadFiber CreateFiber()
        {
            var fiber = new ThreadFiber(ex =>
            {
                Logger.WriteLog(ELogLevel.Error, "Fiber exception occurred: {0}\r\n{1}", ex.Message, ex.StackTrace);
            });
            return fiber;
        }

        private bool SetupConnector(IServerConfig config)
        {
            foreach (var connectorConfig in config.Connectors)
            {
                var connector = CreateConnector(connectorConfig.Name);
                if (null == connector)
                {
                    Logger.WriteLog(ELogLevel.Info, "Failed to create connector {0}.", connectorConfig.Name);
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
                    Logger.WriteLog(e);
                    return false;
                }
            }
            
            return true;
        }

        private bool SetupProtocolLoader()
        {
            var assemblies = new List<Assembly> { Assembly.GetEntryAssembly(), Assembly.GetExecutingAssembly() };
             if (!ProtocolManager.Initialize(assemblies, Logger.WriteLog))
                 return false;
             
             Logger.WriteLog(ELogLevel.Info, "The protocol was successfully loaded. list=[{0}]", string.Join(", ", ProtocolManager.ProtocolNameList)); 
             return true;
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
                Logger.WriteLog(e);
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
                    
                    Logger.WriteLog(ELogLevel.Debug, "Client terminated due to timeout. peerId={0}", peer.PeerId);
                 
                    // 재 연결 타임아웃으로 종료함
                    _waitingReconnectPeerDict.TryRemove(peer.PeerId, out _);
                    peer.LeaveServer(ECloseReason.TimeOut);
                }
            }
            catch (Exception e)
            {
                Logger.WriteLog(e);
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
                peer?.Update();
        }

        public abstract TPeer CreatePeer(IClientSession session, ECryptographicKeySize cryptoKeySize, byte[] cryptoPublicKey);
        protected abstract IConnector CreateConnector(string name);

        public TPeer FindPeer(EPeerId peerId)
        {
            if (!_peerDict.TryGetValue(peerId, out var peer))
                peer = GetWaitingReconnectPeer(peerId);
            return peer;
        }

        protected virtual void UpdatePeer(TPeer peer)
        {
            switch (peer.PeerType)
            {
                case EPeerType.User:
                    _peerDict.TryAdd(peer.PeerId, peer);
                    break;
                case EPeerType.Server:
                    _peerDict.AddOrUpdate(peer.PeerId, peer, (key, value) => peer);
                    break;
                default:
                    throw new ArgumentException($"Invalid peer type: {peer.PeerType}");
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

        protected override void OnSessionClosed(ClientSession<TPeer> session, ECloseReason reason)
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

        internal void OnlinePeer(TPeer peer, IClientSession session)
        {
            RemoveWaitingReconnectPeer(peer);
            UpdatePeer(peer);
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
                Logger.WriteLog(ELogLevel.Error, "Failed to remove peer: {0}", peer);
                return;
            }

            Logger.WriteLog(ELogLevel.Debug, "Peer removed. peerId={0}", removed.PeerId);
            removed.LeaveServer(reason);
        }

        private void RegisterWaitingReconnectPeer(TPeer peer)
        {
            if (!TryRemovePeer(peer.PeerId, out _))
                return;

            if (!_waitingReconnectPeerDict.TryAdd(peer.PeerId, new WaitingReconnectPeer(peer, Config.WaitingReconnectPeerTimeOutSec)))
                return;
            
            Logger.WriteLog(ELogLevel.Debug, "Peer waiting reconnect registered. peerId={0}", peer.PeerId);
        }

        private void RemoveWaitingReconnectPeer(TPeer peer)
        {
            if (!_waitingReconnectPeerDict.TryRemove(peer.PeerId, out _))
                return;
            
            Logger.WriteLog(ELogLevel.Debug, "Peer waiting reconnect removed. peerId={0}", peer.PeerId);
        }

        public TPeer GetWaitingReconnectPeer(EPeerId peerId)
        {
            _waitingReconnectPeerDict.TryGetValue(peerId, out var waitingReconnectPeer);
            return waitingReconnectPeer?.Peer;
        }
    }
}
