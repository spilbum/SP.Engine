using SP.Engine.Runtime;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server;
using SP.Sample.Common;

namespace SP.Sample.GameServer.UserPeer;

public partial class GamePeer(ISession session) : BasePeer(PeerKind.User, session)
{
    public long Uid { get; private set; }
    public string Name { get; private set; } = "none";

    protected override void OnLeaveServer(CloseReason reason)
    {
        var rr = _roomReference;
        if (rr != null)
            LeaveRoom(rr.Room.RoomId, RoomLeaveReason.SessionClosed);

        GameServer.Instance.Matchmaker.Cancel(this);
    }

    public void SetUid(long uid)
    {
        Uid = uid;
        Name = $"User_{uid:D6}";
    }

    public RankProfileInfo ToRankProfileInfo()
    {
        return new RankProfileInfo
        {
            Name = Name,
            CountryCode = "--",
            Level = 0
        };
    }

    public void ExecuteProtocol(IProtocolData protocol)
    {
        try
        {
            IProtocolData? ack = null;
            switch (protocol)
            {
                case C2GProtocolData.RoomCreateReq req:
                    ack = HandleRoomCreate(req);
                    break;
                case C2GProtocolData.RoomSearchReq req:
                    ack = HandleRoomSearch(req);
                    break;
                case C2GProtocolData.RoomRandomSearchReq req:
                    ack = HandleRoomRandomSearch(req);
                    break;
                case C2GProtocolData.RoomJoinReq req:
                    ack = HandleRoomJoin(req);
                    break;
                case C2GProtocolData.RoomLeaveNtf notify:
                    HandleRoomLeave(notify);
                    break;
                case C2GProtocolData.GameReadyCompletedNtf notify:
                    HandleGameReadyCompleted(notify);
                    break;
                case C2GProtocolData.GameActionReq req:
                    ack = HandleGameAction(req);
                    break;
            }

            if (ack != null)
                Send(ack);
        }
        catch (Exception e)
        {
            Logger.Error(e, "Protocol execution failed. uid={0}", Uid);
        }
    }
}
