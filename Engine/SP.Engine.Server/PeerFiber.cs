using System;
using System.Collections.Generic;
using SP.Core.Fiber;
using SP.Core.Logging;

namespace SP.Engine.Server;

public sealed class PeerFiber : IDisposable
{
    private readonly List<BasePeer> _peers = [];
    private readonly IFiber _fiber;
    private readonly IScheduler _globalScheduler;
    private readonly ILogger _logger;
    private readonly IDisposable _tickTimer;
    
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
        
            var lastIndex = _peers.Count - 1;
            if (index != lastIndex) _peers[index] = _peers[lastIndex];
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
    
    public void Enqueue(Action action) => _fiber.Enqueue(action);
    public void Enqueue<T>(Action<T> action, T state) => _fiber.Enqueue(action, state);
    public void Enqueue<T1, T2>(Action<T1, T2> action, T1 s1, T2 s2) => _fiber.Enqueue(action, s1, s2);
    public void Enqueue<T1, T2, T3>(Action<T1, T2, T3> action, T1 s1, T2 s2, T3 s3) => _fiber.Enqueue(action, s1, s2, s3);
    
    public IDisposable Schedule(Action action, TimeSpan due, TimeSpan period) 
        => _globalScheduler.Schedule(_fiber, action, due, period);
    public IDisposable Schedule<T>(Action<T> action, T state, TimeSpan due, TimeSpan period) 
        => _globalScheduler.Schedule(_fiber, action, state, due, period);
    public IDisposable Schedule<T1, T2>(Action<T1, T2> action, T1 s1, T2 s2, TimeSpan due, TimeSpan period)
        => _globalScheduler.Schedule(_fiber, action, s1, s2, due, period);
    public IDisposable Schedule<T1, T2, T3>(Action<T1, T2, T3> action, T1 s1, T2 s2, T3 s3, TimeSpan due, TimeSpan period)
        => _globalScheduler.Schedule(_fiber, action, s1, s2, s3, due, period);

    public void Dispose()
    {
        _tickTimer.Dispose();
        _fiber.Dispose();
    }
}
