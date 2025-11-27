using System.Collections.Generic;
using SP.Engine.Runtime.Protocol;

namespace Common
{
    public static class G2RProtocol
    {
        public const ushort RankUpdateReq = 3000;
        public const ushort RankTopReq = 3001;
        public const ushort RankRangeReq = 3002;
        public const ushort RankMyReq = 3003;
        
        public const ushort ServerSyncNtf = 3999;
    }

    public static class R2GProtocol
    {
        public const ushort RankUpdateAck = 4000;
        public const ushort RankTopAck = 4001;
        public const ushort RankRangeAck = 4002;
        public const ushort RankMyAck = 4003;
    }

    public static class G2RProtocolData
    {
        [Protocol(G2RProtocol.ServerSyncNtf)]
        public class ServerSyncNtf : BaseProtocolData
        {
            public int UserCount;
        }
        
        [Protocol(G2RProtocol.RankUpdateReq)]
        public class RankUpdateReq : BaseProtocolData
        {
            public int? AbsoluteScore;
            public int DeltaScore;
            public RankProfileInfo? Profile;
            public SeasonKind SeasonKind;
            public long Uid;
        }

        [Protocol(G2RProtocol.RankTopReq)]
        public class RankTopReq : BaseProtocolData
        {
            public int Count;
            public SeasonKind SeasonKind;
            public long Uid;
        }

        [Protocol(G2RProtocol.RankRangeReq)]
        public class RankRangeReq : BaseProtocolData
        {
            public int Count;
            public SeasonKind SeasonKind;
            public int StartRank;
            public long Uid;
        }

        [Protocol(G2RProtocol.RankMyReq)]
        public class RankMyReq : BaseProtocolData
        {
            public SeasonKind SeasonKind;
            public long Uid;
        }
    }

    public static class R2GProtocolData
    {
        [Protocol(R2GProtocol.RankUpdateAck)]
        public class RankUpdateAck : BaseProtocolData
        {
            public ErrorCode Result;
            public SeasonKind SeasonKind;
            public long Uid;
        }

        [Protocol(R2GProtocol.RankTopAck, compress: Toggle.On)]
        public class RankTopAck : BaseProtocolData
        {
            public List<PlayerRankInfo>? Infos;
            public ErrorCode Result;
            public SeasonKind SeasonKind;
            public long Uid;
        }

        [Protocol(R2GProtocol.RankRangeAck, compress: Toggle.On)]
        public class RankRangeAck : BaseProtocolData
        {
            public List<PlayerRankInfo>? Infos;
            public ErrorCode Result;
            public SeasonKind SeasonKind;
            public long Uid;
        }

        [Protocol(R2GProtocol.RankMyAck)]
        public class RankMyAck : BaseProtocolData
        {
            public PlayerRankInfo? Info;
            public int Rank;
            public ErrorCode Result;
            public int Score;
            public SeasonKind SeasonKind;
            public long Uid;
        }
    }
}
