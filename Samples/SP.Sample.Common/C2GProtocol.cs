using System.Collections.Generic;
using SP.Engine.Runtime.Protocol;

namespace SP.Sample.Common
{
    public static class C2GProtocol
    {
        public const ushort LoginReq = 10000;
        public const ushort RoomCreateReq = 10001;
        public const ushort RoomSearchReq = 10002;
        public const ushort RoomRandomSearchReq = 10003;
        public const ushort RoomJoinReq = 10004;
        public const ushort RoomLeaveNtf = 10005;

        public const ushort GameReadyCompletedNtf = 10006;
        public const ushort GameActionReq = 10007;

        public const ushort RankMyReq = 10008;
        public const ushort RankTopReq = 10009;
        public const ushort RankRangeReq = 10010;
    }

    public static class G2CProtocol
    {
        public const ushort LoginAck = 20000;

        public const ushort RoomCreateAck = 20001;
        public const ushort RoomSearchAck = 20002;
        public const ushort RoomRandomSearchAck = 20003;
        public const ushort RoomJoinAck = 20004;
        public const ushort RoomMemberEnterNtf = 20005;
        public const ushort RoomMemberLeaveNtf = 20006;

        public const ushort GameReadyNtf = 20007;
        public const ushort GameStartNtf = 20008;
        public const ushort GameEndNtf = 20009;
        public const ushort GameActionAck = 20010;

        public const ushort RankMyAck = 20011;
        public const ushort RankTopAck = 20012;
        public const ushort RankRangeAck = 20013;
    }

    public static class C2GProtocolData
    {
        [Protocol(C2GProtocol.LoginReq)]
        public class LoginReq : BaseProtocolData
        {
            public string? AccessToken;
            public string? CountryCode;
            public string? Name;
            public long Uid;
        }

        [Protocol(C2GProtocol.RoomCreateReq)]
        public class RoomCreateReq : BaseProtocolData
        {
            public RoomOptionsInfo? Options;
        }

        [Protocol(C2GProtocol.RoomSearchReq)]
        public class RoomSearchReq : BaseProtocolData
        {
            public long RoomId;
        }

        [Protocol(C2GProtocol.RoomRandomSearchReq)]
        public class RoomRandomSearchReq : BaseProtocolData
        {
            public RoomOptionsInfo? Options;
        }

        [Protocol(C2GProtocol.RoomJoinReq)]
        public class RoomJoinReq : BaseProtocolData
        {
            public long RoomId;
        }

        [Protocol(C2GProtocol.RoomLeaveNtf)]
        public class RoomLeaveNtf : BaseProtocolData
        {
            public RoomLeaveReason Reason;
        }

        [Protocol(C2GProtocol.GameReadyCompletedNtf)]
        public class GameReadyCompletedNtf : BaseProtocolData
        {
        }

        [Protocol(C2GProtocol.GameActionReq)]
        public class GameActionReq : BaseProtocolData
        {
            public ActionKind Action;
            public int SeqNo;
            public string? Value;
        }

        [Protocol(C2GProtocol.RankTopReq)]
        public class RankTopReq : BaseProtocolData
        {
            public int Count;
            public SeasonKind SeasonKind;
        }

        [Protocol(C2GProtocol.RankMyReq)]
        public class RankMyReq : BaseProtocolData
        {
            public SeasonKind SeasonKind;
        }

        [Protocol(C2GProtocol.RankRangeReq)]
        public class RankRangeReq : BaseProtocolData
        {
            public int Count;
            public SeasonKind SeasonKind;
            public int StartRank;
        }
    }

    public static class G2CProtocolData
    {
        [Protocol(G2CProtocol.LoginAck)]
        public class LoginAck : BaseProtocolData
        {
            public string? AccessToken;
            public ErrorCode Result;
            public long Uid;
        }

        [Protocol(G2CProtocol.RoomCreateAck)]
        public class RoomCreateAck : BaseProtocolData
        {
            public RoomOptionsInfo? Options;
            public ErrorCode Result;
            public long RoomId;
            public string? ServerIp;
            public int ServerPort;
        }

        [Protocol(G2CProtocol.RoomSearchAck)]
        public class RoomSearchAck : BaseProtocolData
        {
            public RoomOptionsInfo? Options;
            public ErrorCode Result;
            public long RoomId;
            public string? ServerIp;
            public int ServerPort;
        }

        [Protocol(G2CProtocol.RoomRandomSearchAck)]
        public class RoomRandomSearchAck : BaseProtocolData
        {
            public RoomOptionsInfo? Options;
            public ErrorCode Result;
            public long RoomId;
            public string? ServerIp;
            public int ServerPort;
        }

        [Protocol(G2CProtocol.RoomJoinAck)]
        public class RoomJoinAck : BaseProtocolData
        {
            public List<RoomMemberInfo>? Members;
            public ErrorCode Result;
            public long RoomId;
            public RoomKind RoomKind;
        }

        [Protocol(G2CProtocol.RoomMemberEnterNtf)]
        public class RoomMemberEnterNtf : BaseProtocolData
        {
            public RoomMemberInfo? Member;
            public long RoomId;
            public int RoomMemberCount;
        }

        [Protocol(G2CProtocol.RoomMemberLeaveNtf)]
        public class RoomMemberLeaveNtf : BaseProtocolData
        {
            public RoomLeaveReason Reason;
            public long RoomId;
            public int RoomMemberCount;
            public long Uid;
        }

        [Protocol(G2CProtocol.GameReadyNtf)]
        public class GameReadyNtf : BaseProtocolData
        {
        }

        [Protocol(G2CProtocol.GameStartNtf)]
        public class GameStartNtf : BaseProtocolData
        {
        }

        [Protocol(G2CProtocol.GameEndNtf)]
        public class GameEndNtf : BaseProtocolData
        {
            public byte Rank;
            public ItemInfo? Reward;
        }

        [Protocol(G2CProtocol.GameActionAck)]
        public class GameActionAck : BaseProtocolData
        {
            public ErrorCode Result;
            public int SeqNo;
        }

        [Protocol(G2CProtocol.RankTopAck)]
        public class RankTopAck : BaseProtocolData
        {
            public List<PlayerRankInfo>? Infos;
            public ErrorCode Result;
            public SeasonKind SeasonKind;
        }

        [Protocol(G2CProtocol.RankMyAck)]
        public class RankMyAck : BaseProtocolData
        {
            public PlayerRankInfo? Info;
            public int Rank;
            public ErrorCode Result;
            public int Score;
            public SeasonKind SeasonKind;
        }

        [Protocol(G2CProtocol.RankRangeAck)]
        public class RankRangeAck : BaseProtocolData
        {
            public List<PlayerRankInfo>? Infos;
            public ErrorCode Result;
            public SeasonKind SeasonKind;
        }
    }
}
