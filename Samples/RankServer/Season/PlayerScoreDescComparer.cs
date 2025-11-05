using SP.Shared.Rank.Season;

namespace RankServer.Season;

public class PlayerScoreDescComparer : BaseRankSeasonRecordComparer<PlayerRankRecord>
{
    public override int Compare(PlayerRankRecord? x, PlayerRankRecord? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return 1;
        if (y is null) return -1;

        var compare = y.Score.CompareTo(x.Score);
        if (compare != 0) return compare;

        compare = x.UpdateUtc.CompareTo(y.UpdateUtc);
        return compare != 0 ? compare : x.Key.CompareTo(y.Key);
    }
}
