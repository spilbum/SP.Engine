using Common;
using GameServer.Room;

namespace GameServer.UserPeer;

public partial class GamePeer
{
    private RoomReference? _roomReference;

    public RoomMemberInfo ToRoomMember()
    {
        return new RoomMemberInfo { UserId = Uid, Name = Name };
    }

    private void LeaveRoom(long roomId, RoomLeaveReason reason)
    {
        var rr = _roomReference;
        if (rr == null)
            return;

        if (roomId < 0 || roomId != rr.Room.RoomId)
            return;

        ((GameRoom)rr.Room).EnqueueLeaveRoom(Uid, reason);
        rr.Dispose();
        _roomReference = null;
    }

    private G2CProtocolData.RoomCreateAck HandleRoomCreate(C2GProtocolData.RoomCreateReq req)
    {
        var ack = new G2CProtocolData.RoomCreateAck { Result = ErrorCode.Unknown };

        try
        {
            var roomId = RoomIdAllocator.Generate();

            using var _ = GameServer.Instance.RoomManager.EnsureRoom(
                roomId,
                req.Options,
                TimeSpan.FromSeconds(10));

            if (!GameServer.Instance.RoomManager.TryGet(roomId, out var room) || room == null)
            {
                ack.Result = ErrorCode.InternalError;
                return ack;
            }

            ack.Result = ErrorCode.Ok;
            ack.RoomId = room.RoomId;
            ack.ServerIp = GameServer.Instance.GetIpAddress();
            ack.ServerPort = GameServer.Instance.GetPort();
            ack.Options = room.ToRoomOptionsInfo();
            return ack;
        }
        catch (Exception e)
        {
            Logger.Error(e, "Room create failed.");
            ack.Result = ErrorCode.InternalError;
            return ack;
        }
    }

    private G2CProtocolData.RoomSearchAck HandleRoomSearch(C2GProtocolData.RoomSearchReq req)
    {
        var ack = new G2CProtocolData.RoomSearchAck { Result = ErrorCode.Unknown };

        if (req.RoomId <= 0)
        {
            ack.Result = ErrorCode.InvalidRequest;
            return ack;
        }

        if (!GameServer.Instance.RoomManager.TryGet(req.RoomId, out var room) || room == null)
        {
            ack.Result = ErrorCode.RoomNotFound;
            return ack;
        }

        if (room.IsFull)
        {
            ack.Result = ErrorCode.RoomFull;
            return ack;
        }

        if (room.IsDisposed)
        {
            ack.Result = ErrorCode.RoomClosed;
            return ack;
        }

        var rr = _roomReference;
        if (rr != null)
        {
            LeaveRoom(rr.Room.RoomId, RoomLeaveReason.ServerReject);
            _roomReference = null;
        }

        ack.Result = ErrorCode.Ok;
        ack.RoomId = room.RoomId;
        ack.ServerIp = GameServer.Instance.GetIpAddress();
        ack.ServerPort = GameServer.Instance.GetPort();
        ack.Options = room.ToRoomOptionsInfo();
        return ack;
    }

    private G2CProtocolData.RoomRandomSearchAck? HandleRoomRandomSearch(C2GProtocolData.RoomRandomSearchReq req)
    {
        var spec = req.Options ?? new RoomOptionsInfo();
        GameServer.Instance.Matchmaker.Enqueue(this, spec, (code, roomId) =>
        {
            if (!IsConnected) return;

            var ack = new G2CProtocolData.RoomRandomSearchAck { Result = code };
            if (code == ErrorCode.Ok && roomId > 0)
            {
                ack.RoomId = roomId.Value;
                ack.ServerIp = GameServer.Instance.GetIpAddress();
                ack.ServerPort = GameServer.Instance.GetPort();
                if (GameServer.Instance.RoomManager.TryGet(roomId.Value, out var room) && room != null)
                    ack.Options = room.ToRoomOptionsInfo();
            }

            Send(ack);
        });
        return null;
    }

    private G2CProtocolData.RoomJoinAck? HandleRoomJoin(C2GProtocolData.RoomJoinReq req)
    {
        var ack = new G2CProtocolData.RoomJoinAck { Result = ErrorCode.Unknown };

        if (req.RoomId <= 0)
        {
            ack.Result = ErrorCode.InvalidRequest;
            return ack;
        }

        var rr = _roomReference;
        if (rr != null) LeaveRoom(rr.Room.RoomId, RoomLeaveReason.ServerReject);

        rr = GameServer.Instance.RoomManager.AcquireRoomReference(req.RoomId, this);
        if (rr.Room is GameRoom room)
            room.EnqueueProtocol(this, req);
        _roomReference = rr;
        return null;
    }

    private void HandleRoomLeave(C2GProtocolData.RoomLeaveNtf notify)
    {
        var rr = _roomReference;
        if (rr == null)
            return;

        if (rr.Room is GameRoom room)
            room.EnqueueProtocol(this, notify);

        rr.Dispose();
        _roomReference = null;
    }

    private void HandleGameReadyCompleted(C2GProtocolData.GameReadyCompletedNtf notify)
    {
        var rr = _roomReference;
        if (rr is { Room: GameRoom room })
            room.EnqueueProtocol(this, notify);
    }

    private G2CProtocolData.GameActionAck? HandleGameAction(C2GProtocolData.GameActionReq req)
    {
        var rr = _roomReference;
        if (rr is not { Room: GameRoom room })
        {
            var ack = new G2CProtocolData.GameActionAck { Result = ErrorCode.InternalError, SeqNo = req.SeqNo };
            return ack;
        }

        room.EnqueueProtocol(this, req);
        return null;
    }
}
