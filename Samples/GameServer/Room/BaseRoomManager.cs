using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using SP.Core.Fiber;
using SP.Engine.Server;
using SP.Engine.Server.Logging;

namespace GameServer.Room;

public abstract class BaseRoomManager : IDisposable
{
    private readonly ConcurrentDictionary<long, RoomEntry> _entries = new();
    private readonly ConcurrentDictionary<long, RoomPin> _roomPins = new();

    private readonly FiberScheduler _scheduler;

    protected BaseRoomManager()
    {
        var logger = LogManager.GetLogger();
        _scheduler = new FiberScheduler(logger, "RoomManager Scheduler");
    }

    public virtual void Dispose()
    {
        foreach (var e in _entries.Values)
            e.Room.Close();
        foreach (var p in _roomPins.Values)
            p.Dispose();

        _entries.Clear();
        _roomPins.Clear();
        _scheduler.Dispose();
        GC.SuppressFinalize(this);
    }

    public virtual void Stop()
    {
        _scheduler.Dispose();
    }

    public IDisposable EnsureRoom(long roomId, object? context, TimeSpan timeout)
    {
        var room = CreateRoom(roomId, context);
        var entry = new RoomEntry(room);
        if (!_entries.TryAdd(roomId, entry))
            throw new InvalidOperationException($"Duplicated roomId detected. RoomId={roomId}");

        OnRoomRegistered(room);
        LogManager.Debug("Room ensured: RoomId={0}", roomId);

        var pin = new RoomPin(this, roomId);
        _roomPins[roomId] = pin;

        if (timeout <= TimeSpan.Zero) return pin;
        _scheduler.Schedule(RemovePin, roomId, timeout, TimeSpan.Zero);
        return pin;
    }

    private void RemovePin(long roomId)
    {
        if (_roomPins.TryRemove(roomId, out var pin))
            pin.Dispose();
    }

    private void AddPin(long roomId)
    {
        if (!_entries.TryGetValue(roomId, out var entry)) return;
        entry.AddPin();
    }

    private void ReleasePin(long roomId)
    {
        if (!_entries.TryGetValue(roomId, out var entry)) return;
        entry.ReleasePin();
        TryEvictEntry(entry);
    }

    public RoomReference AcquireRoomReference(long roomId, IPeer peer)
    {
        if (!_entries.TryGetValue(roomId, out var entry))
            throw new InvalidOperationException($"Room not found: {roomId}");
        var rr = entry.AddReference(this, peer);
        RemovePin(roomId);
        return rr;
    }

    internal void ReleaseRoomReference(RoomReference rr)
    {
        if (!_entries.TryGetValue(rr.Room.RoomId, out var entry)) return;
        entry.ReleaseReference(rr.Peer);
        TryEvictEntry(entry);
    }

    private void TryEvictEntry(RoomEntry entry)
    {
        var room = entry.Room;

        if (entry.ReferenceCount > 0) return;
        if (entry.PinCount > 0) return;

        var removeNow = room.OnIdle();
        if (!removeNow) return;

        if (!_entries.TryRemove(room.RoomId, out _)) return;
        try
        {
            OnRoomRemoved(room);
        }
        finally
        {
            room.Close();
        }
    }

    internal void TryEvictRoomImmediate(BaseRoom room)
    {
        if (!_entries.TryGetValue(room.RoomId, out var entry)) return;
        if (entry.ReferenceCount > 0 || entry.PinCount > 0) return;
        if (!_entries.TryRemove(room.RoomId, out _)) return;

        try
        {
            OnRoomRemoved(room);
        }
        finally
        {
            room.Close();
        }
    }

    protected abstract BaseRoom CreateRoom(long roomId, object? context);

    protected virtual void OnRoomRegistered(BaseRoom room)
    {
    }

    protected virtual void OnRoomRemoved(BaseRoom room)
    {
    }

    private sealed class RoomEntry(BaseRoom room)
    {
        private readonly Dictionary<IPeer, RoomReference> _refs = new(ReferenceEqualityComparer<IPeer>.Instance);
        private readonly object _syncRoot = new();
        private int _pinCount;

        public BaseRoom Room { get; } = room;

        public int ReferenceCount
        {
            get
            {
                lock (_syncRoot)
                {
                    return _refs.Count;
                }
            }
        }

        public int PinCount => Volatile.Read(ref _pinCount);

        public void AddPin()
        {
            Interlocked.Increment(ref _pinCount);
        }

        public void ReleasePin()
        {
            Interlocked.Decrement(ref _pinCount);
        }

        public RoomReference AddReference(BaseRoomManager manager, IPeer peer)
        {
            lock (_syncRoot)
            {
                if (_refs.TryGetValue(peer, out var existed))
                    return existed;

                var rr = new RoomReference(manager, Room, peer);
                _refs.Add(peer, rr);
                LogManager.Debug("Room ref acquired: room={0}, peer={1}, refs={2}", Room.RoomId, peer.PeerId,
                    _refs.Count);
                Room.NotifyActive();
                return rr;
            }
        }

        public void ReleaseReference(IPeer peer)
        {
            lock (_syncRoot)
            {
                _refs.Remove(peer);
                LogManager.Debug("Room ref released: room={0}, peer={1}, refs={2}", Room.RoomId, peer.PeerId,
                    _refs.Count);
            }
        }
    }

    private class RoomPin : IDisposable
    {
        private readonly BaseRoomManager _mgr;
        private readonly long _roomId;
        private bool _disposed;

        public RoomPin(BaseRoomManager mgr, long roomId)
        {
            _mgr = mgr;
            _roomId = roomId;
            _mgr.AddPin(roomId);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _mgr.ReleasePin(_roomId);
            GC.SuppressFinalize(this);
        }
    }
}

internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
{
    public static readonly ReferenceEqualityComparer<T> Instance = new();

    public bool Equals(T? x, T? y)
    {
        return ReferenceEquals(x, y);
    }

    public int GetHashCode(T obj)
    {
        return RuntimeHelpers.GetHashCode(obj);
    }
}
