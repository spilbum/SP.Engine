using System.Collections.Concurrent;
using SP.Core.Fiber;
using SP.Core.Logging;
using SP.Engine.Server.Logging;
using SP.Sample.Common;

namespace SP.Sample.GameServer.Room;

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
    private readonly ConcurrentDictionary<long, GameRoom> _rooms = new();
    private readonly FiberScheduler[] _schedulers;

    public GameRoomManager()
    {
        var cores = Math.Max(1, Environment.ProcessorCount);
        var schedulerCount = Math.Min(64, cores * 2);

        var logger = LogManager.GetLogger();
        _schedulers = CreateRoomScheduler(schedulerCount, logger);
    }

    private static FiberScheduler[] CreateRoomScheduler(int count, ILogger logger)
    {
        var arr = new FiberScheduler[count];
        for (var i = 0; i < count; i++)
            arr[i] = new FiberScheduler(logger, $"RoomScheduler-{i:D2}");
        return arr;
    }

    private FiberScheduler GetRoomScheduler(long roomId)
    {
        var key = unchecked((int)(roomId ^ (roomId >> 32))) & 0x7FFFFFFF;
        var idx = key % _schedulers.Length;
        return _schedulers[idx];
    }

    public override void Stop()
    {
        foreach (var fiber in _schedulers)
            fiber.Dispose();

        base.Stop();
    }

    protected override BaseRoom CreateRoom(long roomId, object? context)
    {
        if (context is not RoomOptionsInfo optionsInfo)
            throw new ArgumentException("Invalid RoomOptionsSpecInfo", nameof(context));

        var fiber = GetRoomScheduler(roomId);
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
