using SP.Engine.Server;

namespace GameServer.Room;

public class RoomReference : IDisposable
{
    private readonly BaseRoomManager _manager;
    private bool _disposed;

    internal RoomReference(BaseRoomManager manager, BaseRoom room, IPeer peer)
    {
        _manager = manager;
        Room = room;
        Peer = peer;
    }

    public BaseRoom Room { get; }
    public IPeer Peer { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _manager.ReleaseRoomReference(this);
        }
        catch
        {
            /* ignored */
        }

        GC.SuppressFinalize(this);
    }
}
