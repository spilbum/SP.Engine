using System;
using System.Collections.Concurrent;
using SP.Core.Logging;
using SP.Engine.Runtime;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server;

public class PeerManager(ILogger logger, IEngineConfig config)
{
    private readonly ConcurrentDictionary<uint, BasePeer> _peers = [];
    private readonly ConcurrentDictionary<uint, WaitingReconnectPeer> _waitingPeers = [];

    public BasePeer GetPeer(uint peerId)
    {
        if (_peers.TryGetValue(peerId, out var peer)) return peer;
        _waitingPeers.TryGetValue(peerId, out var waiting);
        return waiting?.Peer;
    }

    public BasePeer GetWaitingPeer(uint peerId)
    {
        _waitingPeers.TryGetValue(peerId, out var waiting);
        return waiting?.Peer;
    }

    public void Join(BasePeer peer)
    {
        if (!_peers.TryAdd(peer.PeerId, peer))
        {
            logger.Warn("Failed to join peer. peerId={0}", peer.PeerId);
            return;
        }
        
        peer.JoinServer();
    }

    public bool Online(BasePeer peer, ISession session)
    {
        // 대기 목록 제거
        if (!_waitingPeers.TryRemove(peer.PeerId, out _))
        {
            logger.Warn($"Reconnection rejected: Peer timed out. peerId={peer.PeerId}");
            return false;
        }

        if (!_peers.TryAdd(peer.PeerId, peer))
        {
            logger.Error("Failed to add peer to active list. peerId={0}", peer.PeerId);
            peer.LeaveServer(CloseReason.InternalError);
            return false;
        }

        peer.Online(session);
        return true;
    }

    public void Offline(BasePeer peer, CloseReason reason)
    {
        if (!_peers.TryRemove(peer.PeerId, out _))
        {
            logger.Error("Failed to remove peer from active list: {0}", peer.PeerId);
            return;
        }

        // 대기 목록 추가
        var waiting = new WaitingReconnectPeer(peer, config.Session.WaitingReconnectTimeoutSec);
        if (!_waitingPeers.TryAdd(peer.PeerId, waiting))
        {
            logger.Error("Failed to register waiting reconnect peer. peerId={0}", peer.PeerId);
            peer.LeaveServer(CloseReason.InternalError);
            return;
        }

        logger.Debug("Peer waiting reconnect registered. peerId={0}", peer.PeerId);
        peer.Offline(reason);
    }

    public void RemovePeer(BasePeer peer, CloseReason reason)
    {
        _peers.TryRemove(peer.PeerId, out _);
        _waitingPeers.TryRemove(peer.PeerId, out _);

        peer.LeaveServer(reason);
    }

    public bool ChangeServerPeer(BasePeer newPeer)
    {
        if (newPeer.Kind != PeerKind.Server)
        {
            logger.Error($"Change failed: Invalid peer kind={newPeer.Kind}");
            return false;
        }
        
        if (!_peers.TryGetValue(newPeer.PeerId, out var oldPeer))
        {
            logger.Error($"Change failed: Peer not found in active list. peerId={newPeer.PeerId}");
            return false;
        }

        if (oldPeer.Session is Session session)
        {
            session.UpdatePeer(newPeer);
        }
        else
        {
            logger.Error($"Change failed: Old peer has no valid session. peerId={newPeer.PeerId}");
            return false;
        }
        
        _peers[newPeer.PeerId] = newPeer;
        return true;
    }

    public void CheckTimeouts()
    {
        var now = DateTime.UtcNow;

        foreach (var (peerId, info) in _waitingPeers)
        {
            if (now < info.ExpireTime)
                continue;

            if (!_waitingPeers.TryRemove(peerId, out var removed)) 
                continue;
            
            logger.Debug("Client terminated due to timeout. peerId={0}", peerId);
            removed.Peer.LeaveServer(CloseReason.TimeOut);
        }
    }
    
    private sealed class WaitingReconnectPeer(BasePeer peer, int timeOutSec)
    {
        public BasePeer Peer { get; } = peer;
        public DateTime ExpireTime { get; } = DateTime.UtcNow.AddSeconds(timeOutSec);
    }
}
