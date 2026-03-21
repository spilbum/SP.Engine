using System.Collections.Concurrent;
using Common;
using SP.Core.Fiber;
using SP.Core.Logging;
using SP.Engine.Server;
using SP.Engine.Server.Logging;

namespace GameServer.Room;

public static class RoomIdAllocator
{
    private static long _roomId;

    public static long Generate(byte serverId = 0)
    {
        var str = $"{DateTime.UtcNow:yyMMddHHmm}{serverId:D2}00";
        if (!long.TryParse(str, out var id))
            throw new InvalidOperationException($"Failed to parse room ID base string: {str}");

        if (Volatile.Read(ref _roomId) < id)
            Interlocked.Exchange(ref _roomId, id);
        else
            Interlocked.Increment(ref _roomId);
        return Volatile.Read(ref _roomId);
    }
}

public sealed class GameRoomManager : BaseRoomManager
{
    private readonly List<ThreadFiber> _fibers = [];
    private readonly ConcurrentDictionary<long, GameRoom> _rooms = new();
    private int _nextFiberIndex;

    public GameRoomManager()
    {
        var cores = Math.Max(1, Environment.ProcessorCount);
        var fiberCount = Math.Min(64, cores * 2);

        for (var i = 0; i < fiberCount; i++)
        {
            var fiber = new ThreadFiber($"RoomFiber_{i:D2}",
                onError: LogManager.Error);

            _fibers.Add(fiber);
        }
    }

    public override void Dispose()
    {
        foreach (var fiber in _fibers) fiber.Dispose();
        _fibers.Clear();
        
        base.Dispose();
    }

    protected override BaseRoom CreateRoom(long roomId, object? args)
    {
        if (args is not RoomOptionsInfo optionsInfo)
            throw new ArgumentException("Invalid RoomOptionsSpecInfo", nameof(args));

        var index = Interlocked.Increment(ref _nextFiberIndex) % _fibers.Count;
        var fiber = _fibers[index];
        var options = RoomOptionsResolver.Resolve(optionsInfo);

        return new GameRoom(
            this,
            fiber,
            roomId,
            TimeSpan.FromSeconds(15),
            options);
    }

    protected override void OnRoomRegistered(BaseRoom room)
    {
        if (room is GameRoom gr)
            _rooms[gr.RoomId] = gr;
    }

    protected override void OnRoomRemoved(BaseRoom room)
    {
        if (_rooms.TryRemove(room.RoomId, out _))
            LogManager.Info("[RoomManager] Room removed: {0}", room.RoomId);
    }

    public IEnumerable<GameRoom> Matches(RoomOptionsInfo options)
    {
        var rooms = _rooms.Values.Where(r => r.CanJoin(options));
        var list = rooms.OrderByDescending(r => r.MemberCount)
            .ThenBy(r => r.CreatedUtc)
            .ThenBy(r => r.RoomId)
            .ToList();
        return list;
    }

    public bool TryGet(long roomId, out GameRoom? room)
    {
        return _rooms.TryGetValue(roomId, out room);
    }
}
