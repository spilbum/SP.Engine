using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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

    public (double avgDwell, double avgExec, int pendingTotal) GetGlobalFiberMetrics()
    {
        var globalSumDwellMs = 0d;
        var globalSumExecMs = 0d;
        var globalCompletedCount = 0;
        var globalPendingJobs = 0;

        foreach (var peer in _activePeers.Values)
        {
            var metrics = peer.ExtractMetrics();
            globalSumDwellMs += metrics.totalDwell;
            globalSumExecMs += metrics.totalExec;
            globalCompletedCount += metrics.count;
            
            globalPendingJobs += peer.PendingJobCount;
        }

        globalPendingJobs += _reconnectPendingPeers.Values.Sum(waiting => waiting.Peer.PendingJobCount);

        if (globalCompletedCount == 0)
            return (0, 0, globalPendingJobs);
        
        return (
            globalSumDwellMs / globalCompletedCount, 
            globalSumExecMs / globalCompletedCount,
            globalPendingJobs);
    }
    
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
        {
            logger.Warn("No peer to reconnect pending: {0}, sessionId={1}", peerId, session.SessionId);
            return false;
        }

        var peer = pending.Peer;
        if (!_activePeers.TryAdd(peerId, peer))
        {
            logger.Warn("Failed to add active: {0}, sessionId={1}", peerId, session.SessionId);
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
            logger.Debug("Reconnect timeout for PeerId: {0}.", peerId);
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
