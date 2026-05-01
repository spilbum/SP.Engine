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
    private readonly ConcurrentDictionary<uint, BasePeer> _activePeers = [];
    private readonly ConcurrentDictionary<uint, WaitingReconnectPeer> _reconnectQueue = [];

    public BasePeer GetActivePeer(uint peerId)
        => _activePeers.GetValueOrDefault(peerId);

    public BasePeer GetWaitingPeer(uint peerId)
        => _reconnectQueue.TryGetValue(peerId, out var waiting) ? waiting.Peer : null;

    public BasePeer GetAnyPeer(uint peerId)
        => _activePeers.TryGetValue(peerId, out var peer) 
            ? peer 
            : _reconnectQueue.TryGetValue(peerId, out var waiting) ? waiting.Peer : null;

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

        globalPendingJobs += _reconnectQueue.Values.Sum(waiting => waiting.Peer.PendingJobCount);

        if (globalCompletedCount == 0)
            return (0, 0, globalPendingJobs);
        
        return (
            globalSumDwellMs / globalCompletedCount, 
            globalSumExecMs / globalCompletedCount,
            globalPendingJobs);
    }
    
    public void Register(BasePeer peer)
    {
        if (!_activePeers.TryAdd(peer.PeerId, peer))
        {
            logger.Warn("Register failed: Peer {0} already exists.", peer.PeerId);
            peer.Close(CloseReason.InternalError);
            return;
        }
        
        peer.JoinServer();
    }

    public bool TransitionToOnline(uint peerId, ISession session)
    {
        // 대기 목록에서 먼저 제거
        if (!_reconnectQueue.TryRemove(peerId, out var waiting))
            return false;

        var peer = waiting.Peer;
        if (!_activePeers.TryAdd(peerId, peer))
        {
            peer.Close(CloseReason.InternalError);
            return false;
        }

        peer.Online(session);
        return true;
    }

    public void TransitionToOffline(BasePeer peer, CloseReason reason)
    {
        if (!_activePeers.TryRemove(peer.PeerId, out _))
            return;

        // 재접속 대기열 추가
        var timeout = config.Session.WaitingReconnectTimeoutSec;
        var waiting = new WaitingReconnectPeer(peer, timeout);
        
        if (!_reconnectQueue.TryAdd(peer.PeerId, waiting))
        {
            peer.LeaveServer(CloseReason.InternalError);
            return;
        }

        peer.Offline(reason);
    }

    public void Terminate(uint peerId, CloseReason reason)
    {
        _activePeers.TryRemove(peerId, out var p1);
        _reconnectQueue.TryRemove(peerId, out var p2);

        var target = p1 ?? p2?.Peer;
        target?.LeaveServer(reason);
    }

    public bool TransitionTo(BasePeer newPeer)
    {
        return _activePeers.TryGetValue(newPeer.PeerId, out var oldPeer) 
               && _activePeers.TryUpdate(newPeer.PeerId, newPeer, oldPeer);
    }

    public void Update()
    {
        if (_reconnectQueue.IsEmpty) return;
        
        var now = DateTime.UtcNow;
        foreach (var (peerId, info) in _reconnectQueue)
        {
            if (now < info.ExpireTime) continue;

            if (_reconnectQueue.TryRemove(peerId, out var expired))
            {
                expired.Peer.LeaveServer(CloseReason.ServerClosing);
            }
        }
    }
    
    private sealed class WaitingReconnectPeer(BasePeer peer, int timeOutSec)
    {
        public BasePeer Peer { get; } = peer;
        public DateTime ExpireTime { get; } = DateTime.UtcNow.AddSeconds(timeOutSec);
    }
}
