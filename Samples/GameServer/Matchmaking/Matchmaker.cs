using System.Runtime.CompilerServices;
using Common;
using GameServer.Room;
using GameServer.UserPeer;
using SP.Core.Fiber;
using SP.Engine.Server.Logging;

namespace GameServer.Matchmaking;

public class Matchmaker : IDisposable
{
    private readonly Dictionary<GamePeer, Ticket> _pending =
        new(ReferenceEqualityComparer<GamePeer>.Instance);

    private readonly GameRoomManager _roomManager;
    private readonly FiberScheduler _scheduler;

    private readonly TimeSpan _searchingTimeout;
    private int _disposed;

    private IDisposable? _tickTimer;

    public Matchmaker(
        GameRoomManager roomManager,
        TimeSpan searchingPeriod,
        TimeSpan searchingTimeout)
    {
        _roomManager = roomManager;
        _searchingTimeout = searchingTimeout;

        var logger = LogManager.GetLogger();
        _scheduler = new FiberScheduler(logger, nameof(Matchmaker));
        _tickTimer = _scheduler.Schedule(Searching, TimeSpan.Zero, searchingPeriod);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _pending.Clear();
        _tickTimer?.Dispose();
        _tickTimer = null;
        _scheduler.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Enqueue(GamePeer peer, RoomOptionsInfo options, Action<ErrorCode, long?> callback)
    {
        _scheduler.TryEnqueue(() =>
        {
            if (_disposed != 0) return;
            if (_pending.TryGetValue(peer, out var ticket))
            {
                LogManager.Debug("Match req ignored (duplicate). peer={0}", peer.Uid);
                SafeCallback(ticket, ErrorCode.MatchAlreadyInProgress, null);
                return;
            }

            ticket = new Ticket(peer, options, callback);
            _pending[peer] = ticket;
            LogManager.Debug("Match enqueued. peer={0}", peer.Uid);
        });
    }

    public void Cancel(GamePeer peer)
    {
        _scheduler.TryEnqueue(() =>
        {
            if (_disposed != 0) return;
            if (!_pending.TryGetValue(peer, out var t)) return;

            t.Cancel();
            _pending.Remove(peer);
            LogManager.Debug("Match canceled. peer={0}", peer.Uid);
            SafeCallback(t, ErrorCode.MatchCanceled, null);
        });
    }

    private void Searching()
    {
        if (_disposed != 0) return;
        if (_pending.Count == 0) return;

        var now = DateTime.UtcNow;
        var list = _pending.Values.ToList();

        foreach (var t in list)
        {
            if (t.IsCanceled)
            {
                _pending.Remove(t.Peer);
                return;
            }

            var rooms = _roomManager.Matches(t.Options);
            var chosen = rooms.FirstOrDefault();
            if (chosen != null)
            {
                _pending.Remove(t.Peer);
                SafeCallback(t, ErrorCode.Ok, chosen.RoomId);
                LogManager.Debug("Match assigned existing room. peer={0}, room={1}", t.Peer.Uid, chosen.RoomId);
                continue;
            }

            var elapsed = now - t.EnqueuedUtc;
            if (elapsed < _searchingTimeout) continue;

            var newRoomId = RoomIdAllocator.Generate();
            _roomManager.EnsureRoom(newRoomId, t.Options, TimeSpan.FromSeconds(10));
            _pending.Remove(t.Peer);
            SafeCallback(t, ErrorCode.Ok, newRoomId);
            LogManager.Debug("Match created new room. peer={0}, room={1}", t.Peer.Uid, newRoomId);
        }
    }

    private static void SafeCallback(Ticket t, ErrorCode code, long? roomId)
    {
        try
        {
            t.Callback(code, roomId);
        }
        catch (Exception e)
        {
            LogManager.Error(e, "[Matchmaker] Match callback error. peer={0}, room={1}", t.Peer.Uid, roomId);
        }
    }

    private sealed class Ticket(GamePeer peer, RoomOptionsInfo options, Action<ErrorCode, long?> callback)
    {
        public GamePeer Peer { get; } = peer;
        public RoomOptionsInfo Options { get; } = options;
        public Action<ErrorCode, long?> Callback { get; } = callback;
        public DateTime EnqueuedUtc { get; } = DateTime.UtcNow;
        public bool IsCanceled { get; private set; }

        public void Cancel()
        {
            IsCanceled = true;
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
