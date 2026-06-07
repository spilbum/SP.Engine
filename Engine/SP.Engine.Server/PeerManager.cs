using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using SP.Core.Logging;
using SP.Engine.Runtime;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server;

public class PeerManager(ILogger logger, IEngineConfig config)
{
    private readonly ConcurrentDictionary<uint, PeerBase> _activePeers = [];
    private readonly ConcurrentDictionary<uint, PendingReconnect> _reconnectPendingPeers = [];

    public PeerBase GetActivePeer(uint peerId)
        => _activePeers.GetValueOrDefault(peerId);

    public PeerBase GetWaitingPeer(uint peerId)
        => _reconnectPendingPeers.TryGetValue(peerId, out var waiting) ? waiting.Peer : null;

    public PeerBase GetAnyPeer(uint peerId)
        => _activePeers.TryGetValue(peerId, out var peer) 
            ? peer 
            : _reconnectPendingPeers.TryGetValue(peerId, out var waiting) ? waiting.Peer : null;

    public void Register(PeerBase peer)
    {
        if (!_activePeers.TryAdd(peer.PeerId, peer))
        {
            logger.Warn("Register failed: Peer {0} already exists.", peer.PeerId);
            peer.Close(CloseReason.InternalError);
            return;
        }
        
        peer.JoinServer();
    }

    public bool TransitionToOnline(uint peerId, Session session)
    {
        // 대기 목록에서 먼저 제거
        if (!_reconnectPendingPeers.TryRemove(peerId, out var pending))
            return false;

        var peer = pending.Peer;
        if (!_activePeers.TryAdd(peerId, peer))
        {
            peer.Close(CloseReason.InternalError);
            return false;
        }

        peer.Online(session);
        return true;
    }

    public void TransitionToOffline(PeerBase peer, CloseReason reason)
    {
        if (!_activePeers.TryRemove(peer.PeerId, out _))
            return;

        // 재접속 대기열 추가
        var timeout = config.Session.WaitingReconnectTimeoutSec;
        var waiting = new PendingReconnect(peer, timeout);
        
        if (!_reconnectPendingPeers.TryAdd(peer.PeerId, waiting))
        {
            peer.LeaveServer(CloseReason.InternalError);
            return;
        }

        peer.Offline(reason);
    }

    public void Terminate(uint peerId, CloseReason reason)
    {
        if (_activePeers.TryRemove(peerId, out var activePeer))
        {
            activePeer.LeaveServer(reason);
            _reconnectPendingPeers.TryRemove(peerId, out _);
            return;
        }
        
        if (_reconnectPendingPeers.TryRemove(peerId, out var pending))
            pending.Peer.LeaveServer(reason);
    }

    public bool TransitionTo(PeerBase newPeer)
    {
        return _activePeers.TryGetValue(newPeer.PeerId, out var oldPeer) 
               && _activePeers.TryUpdate(newPeer.PeerId, newPeer, oldPeer);
    }

    public void Update()
    {
        if (_reconnectPendingPeers.IsEmpty) return;
        
        var nowUtc = DateTime.UtcNow;
        List<uint> targets = null;
        
        foreach (var kvp in _reconnectPendingPeers)
        {
            if (!kvp.Value.IsExpired(nowUtc)) continue;
            targets ??= [];
            targets.Add(kvp.Key);
        }

        if (targets == null) return;

        foreach (var peerId in targets)
        {
            if (!_reconnectPendingPeers.TryRemove(peerId, out var pending)) continue;
            logger.Debug("Timeout reconnect for peer: {0}.", peerId);
            pending.Peer.LeaveServer(CloseReason.TimeOut);
        }
    }
    
    private readonly struct PendingReconnect(PeerBase peer, int timeoutSec)
    {
        public PeerBase Peer { get; } = peer;
        public DateTime ExpireTime { get; } = DateTime.UtcNow.AddSeconds(timeoutSec);
        public bool IsExpired(DateTime now) => now >= ExpireTime;
    }
}
