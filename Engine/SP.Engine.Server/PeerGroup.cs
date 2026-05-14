using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using SP.Core;
using SP.Core.Fiber;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server;

internal sealed class LogicJob
{
    private ICommand _command;
    private PeerBase _peer;
    private IProtocolData _data;
    private long _enqueuedTimestamp;

    public static LogicJob Rent(PeerBase peer, ICommand command, IProtocolData data, long timestamp)
    {
        var job = SimplePool<LogicJob>.Rent();
        job._command = command;
        job._peer = peer;
        job._data = data;
        job._enqueuedTimestamp = timestamp;
        return job;
    }

    public void Execute(string fiberName, double slowThresholdMs)
    {
        var start = Stopwatch.GetTimestamp();
        var dwellTimeMs = (double)(start - _enqueuedTimestamp) / Stopwatch.Frequency * 1000;

        try
        {
            _command.Execute(_peer, _data);   
        }
        catch (Exception ex)
        {
            _peer.Logger.Error(ex, "LogicJob execution failed: Command={0}, PeerId={1}", _command.Name, _peer.PeerId);
        }
        finally
        {
            var end = Stopwatch.GetTimestamp();
            var executionTimeMs = (double)(end - start) / Stopwatch.Frequency * 1000;

            if (executionTimeMs > slowThresholdMs)
            {
                _peer.Logger.Warn("Slow job detected: Fiber={0}, Command={1}, PeerId={2}, Dwell={3:F2}ms, Exec={4:F2}ms",
                    fiberName, _command.Name, _peer.PeerId, dwellTimeMs, executionTimeMs);
            }
            
            _peer.OnJobCompleted(dwellTimeMs, executionTimeMs);
            ReturnToPool();
        }
    }

    private void ReturnToPool()
    {
        _command = null;
        _peer = null;
        _data = null;
        _enqueuedTimestamp = 0;
        SimplePool<LogicJob>.Return(this);
    }
}

public sealed class PeerGroup : IDisposable
{
    private readonly List<PeerBase> _peers = [];
    private readonly EngineBase _engine;
    
    private readonly ThreadFiber _tickFiber;
    private readonly IDisposable _tickTimer;
    
    private readonly ThreadFiber _logicFiber;

    
    private int _peerCount;
    private volatile bool _disposed;
    
    public int PeerCount => Volatile.Read(ref _peerCount);

    public PeerGroup(byte index, EngineBase engine)
    {
        _engine = engine;
        var config = engine.Config;

        _tickFiber= new ThreadFiber($"PeerTick-{index:D2}",
            maxBatchSize: 256,
            capacity: 1024,
            onError: OnException);
        
        _tickTimer = _engine.GlobalScheduler.Schedule(
            _tickFiber, Tick, TimeSpan.Zero, 
            TimeSpan.FromMilliseconds(config.Session.PeerUpdateIntervalMs));

        _logicFiber = new ThreadFiber($"PeerLogic-{index:D2}",
            maxBatchSize: 1024,
            capacity: 4096,
            onError: OnException);
    }
    
    private void OnException(Exception ex)
    {
        switch (ex)
        {
            case FiberException e:
                _engine.Logger.Error(e.InnerException, "Fiber: {0}, Job: {1}", e.FiberName, e.Job?.Name ?? "unknown");
                break;
            case FiberQueueFullException e:
                if (e.DroppedCount % 100 == 1)
                {
                    _engine.Logger.Warn("Fiber '{0}' is overwhelmed. Pending: {1}, Dropped: {2}",
                        e.FiberName, e.PendingCount, e.DroppedCount);
                }
                break;
        }
    }

    public void AddPeer(PeerBase peer)
    {
        _tickFiber.Enqueue(p =>
        {
            if (_disposed) return;
            _peers.Add(p);
            Interlocked.Increment(ref _peerCount);
            p.SetPeerGroup(this);
        }, peer);
    }

    public void RemovePeer(PeerBase peer)
    {
        _tickFiber.Enqueue(p =>
        {
            var index = _peers.IndexOf(p);
            if (index == -1) return;
            
            p.SetPeerGroup(null);
        
            var lastIndex = _peers.Count - 1;
            if (index != lastIndex)
            {
                _peers[index] = _peers[lastIndex];
            }
            
            _peers.RemoveAt(lastIndex);
            Interlocked.Decrement(ref _peerCount);
        }, peer);
    }

    private void Tick()
    {
        for (var i = _peers.Count - 1; i >= 0; i--)
        {
            try
            {
                _peers[i].Tick();
            }
            catch (Exception e)
            {
                _engine.Logger.Error(e, "Peer tick failed: {0}", _peers[i].PeerId);
            }
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _tickTimer.Dispose();
        _tickFiber.Dispose();
        _logicFiber.Dispose();
    }
    
    public void EnqueueLogicJob(PeerBase peer, ICommand command, IProtocolData data)
    {
        var job = LogicJob.Rent(peer, command, data, Stopwatch.GetTimestamp());
        _logicFiber.Enqueue(ProcessLogicJob, job);
    }

    private void ProcessLogicJob(LogicJob job)
    {
        job.Execute(_logicFiber.Name, _engine.Config.Session.PeerJobSlowThresholdMs);
    }
}
