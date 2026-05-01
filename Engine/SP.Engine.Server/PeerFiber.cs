using System;
using System.Collections.Generic;
using System.Diagnostics;
using SP.Core.Fiber;
using SP.Core.Logging;

namespace SP.Engine.Server;

public sealed class PeerFiber
{
    private readonly List<BasePeer> _peers = [];
    private readonly IFiber _fiber;
    private readonly IScheduler _globalScheduler;
    private readonly ILogger _logger;
    private readonly IDisposable _tickTimer;
    private volatile bool _disposed;
    
    public int PeerCount => _peers.Count;

    public PeerFiber(IFiber fiber, IScheduler globalScheduler, ILogger logger, TimeSpan tickInterval)
    {
        _fiber = fiber;
        _globalScheduler = globalScheduler;
        _logger = logger;
        _tickTimer = globalScheduler.Schedule(fiber, Tick, TimeSpan.Zero, tickInterval);
    }

    public void AddPeer(BasePeer peer)
    {
        _fiber.Enqueue(p =>
        {
            if (_disposed) return;
            _peers.Add(p);
            p.SetFiber(this);
        }, peer);
    }

    public void RemovePeer(BasePeer peer)
    {
        _fiber.Enqueue(p =>
        {
            var index = _peers.IndexOf(p);
            if (index == -1) return;
            
            p.SetFiber(null);
        
            var lastIndex = _peers.Count - 1;
            if (index != lastIndex)
            {
                _peers[index] = _peers[lastIndex];
            }
            
            _peers.RemoveAt(lastIndex);
        }, peer);
    }

    private void Tick()
    {
        for (var i = _peers.Count - 1; i >= 0; i--)
        {
            var peer = _peers[i];
            try
            {
                peer.Tick();
            }
            catch (Exception e)
            {
                _logger.Error($"Peer tick failed: peerId={peer.PeerId}, error={e.Message}{Environment.NewLine}{e.StackTrace}");
            }
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _tickTimer.Dispose();

        _fiber.Enqueue(() =>
        {
            foreach (var peer in _peers)
            {
                peer.SetFiber(null);
            }

            _peers.Clear();
        });
        
        _fiber.Dispose();
    }
    
    public void EnqueueJob<T1, T2>(BasePeer peer, Action<T1, T2> action, T1 s1, T2 s2)
    {
        var job = SimplePool<PeerJob<T1, T2>>.Get();
        job.Peer = peer;
        job.Action = action;
        job.S1 = s1;
        job.S2 = s2;
        job.EnqueuedTimestamp = Stopwatch.GetTimestamp();
        _fiber.Enqueue(ExecuteJob, job);
    }

    private static void ExecuteJob<T1, T2>(PeerJob<T1, T2> job)
    {
        job.Execute();
    }

    private class PeerJob<T1, T2>
    {
        public BasePeer Peer;
        public Action<T1, T2> Action;
        public T1 S1;
        public T2 S2;
        public long EnqueuedTimestamp;

        public void Execute()
        {
            var start = Stopwatch.GetTimestamp();
            var dwellTimeMs = (double)(start - EnqueuedTimestamp) / Stopwatch.Frequency * 1000;
            
            try
            {
                Action?.Invoke(S1, S2);
            }
            finally
            {
                var end = Stopwatch.GetTimestamp();
                var executionTimeMs = (double)(end - start) / Stopwatch.Frequency * 1000;
                
                Peer.OnJobFinished(dwellTimeMs, executionTimeMs);
                ReturnToPool();
            }
        }

        private void ReturnToPool()
        {
            Peer = null;
            Action = null;
            S1 = default;
            S2 = default;
            SimplePool<PeerJob<T1, T2>>.Return(this);
        }
    }
}
