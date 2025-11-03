using SP.Sample.Common;
using SP.Shared.Rank;

namespace SP.Sample.RankServer.Season;

public class PlayerRankFormatter : IRankFormatter<PlayerRankRecord, PlayerRankInfo>
{
    public PlayerRankInfo Format(PlayerRankRecord record, int rank)
    {
        return new PlayerRankInfo
        {
            Rank = rank,
            Uid = record.Uid,
            Score = record.Score,
            Name = record.Name,
            CountryCode = record.CountryCode
        };
    }
}
