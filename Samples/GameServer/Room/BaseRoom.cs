using GameServer.UserPeer;
using SP.Core.Fiber;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Logging;

namespace GameServer.Room;

public abstract class BaseRoom : IDisposable
{
    private readonly TimeSpan _idleTimeout;
    private readonly BaseRoomManager _manager;
    private volatile int _disposed;
    private volatile int _idleArmed;
    private IDisposable? _idleTimer;

    protected BaseRoom(BaseRoomManager manager, IFiberScheduler scheduler, long roomId, TimeSpan idleTimeout)
    {
        _manager = manager;
        Scheduler = scheduler;
        RoomId = roomId;
        _idleTimeout = idleTimeout;
    }

    public long RoomId { get; }
    public DateTime CreatedUtc { get; } = DateTime.UtcNow;
    public bool IsDisposed => _disposed != 0;
    protected IFiberScheduler Scheduler { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        try
        {
            CancelIdleTimer();
            OnDisposed();
        }
        catch (Exception e)
        {
            LogManager.Error(e, "Room dispose failed");
        }

        GC.SuppressFinalize(this);
    }

    public void NotifyActive()
    {
        Scheduler.Enqueue(CancelIdleTimer);
    }

    public bool OnIdle()
    {
        if (_idleTimeout <= TimeSpan.Zero) return true;
        if (Interlocked.CompareExchange(ref _idleArmed, 1, 0) != 0) return false;

        _idleTimer?.Dispose();
        _idleTimer = Scheduler.Schedule(OnIdleTimerFired, _idleTimeout, TimeSpan.Zero);
        LogManager.Debug("Entered idle (armed, timeout={0}ms).", _idleTimeout.TotalMilliseconds);
        return false;
    }

    private void OnIdleTimerFired()
    {
        _idleTimer?.Dispose();
        _idleTimer = null;

        Interlocked.Exchange(ref _idleArmed, 0);
        if (IsDisposed) return;

        try
        {
            _manager.TryEvictRoomImmediate(this);
        }
        catch (Exception e)
        {
            LogManager.Error(e, "Idle eviction failed");
        }
    }

    public void Close()
    {
        Scheduler.Enqueue(() =>
        {
            if (IsDisposed) return;
            try
            {
                OnClosed();
            }
            finally
            {
                Dispose();
            }
        });
    }

    protected virtual void OnClosed()
    {
    }

    protected virtual void OnDisposed()
    {
    }

    public void EnqueueProtocol(GamePeer peer, IProtocolData protocol)
    {
        Scheduler.Enqueue(ExecuteProtocol, peer, protocol);
    }

    protected abstract void ExecuteProtocol(GamePeer peer, IProtocolData protocol);

    private void CancelIdleTimer()
    {
        _idleTimer?.Dispose();
        _idleTimer = null;
        Interlocked.Exchange(ref _idleArmed, 0);
    }
}
