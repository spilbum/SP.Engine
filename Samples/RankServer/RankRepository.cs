using Common;
using DatabaseHandler;

namespace RankServer;

public class RankRepository(MySqlDbConnector connector)
{
    private readonly Dictionary<SeasonKind, Dictionary<int, RankDb.RankSeasonEntity>> _seasons = new();
    private readonly Dictionary<SeasonKind, Dictionary<int, Dictionary<long, RankDb.RankSeasonRecordEntity>>> _records = new();
    private readonly Dictionary<SeasonKind, Dictionary<int, Dictionary<long, RankDb.RankSeasonRewardEntity>>> _rewards = new();

    public RankDb.RankSeasonEntity? GetLatestRankSeason(SeasonKind kind)
    {
        if (connector.CanOpen(DbKind.Rank))
        {
            using var conn = connector.Open(DbKind.Rank);
            return RankDb.LoadLatestSeason(conn, kind);
        }
        
        if (!_seasons.TryGetValue(kind, out var seasons) || seasons.Count == 0)
            return null;

        return seasons.OrderByDescending(kvp => kvp.Key).First().Value;
    }

    public List<RankDb.RankSeasonRecordEntity> GetAllRankSeasonRecords(SeasonKind kind, int seasonNum)
    {
        if (connector.CanOpen(DbKind.Rank))
        {
            using var conn = connector.Open(DbKind.Rank);
            return RankDb.LoadAllSeasonRecords(conn, kind, seasonNum);
        }
        
        var list = new List<RankDb.RankSeasonRecordEntity>();
        
        if (!_records.TryGetValue(kind, out var records) || records.Count == 0)
            return list;

        foreach (var kvp in records)
        {
            list.AddRange(kvp.Value.Select(x => x.Value));
        }
        
        return list;
    }

    public List<RankDb.RankSeasonRewardEntity> GetAllRankSeasonRewards(SeasonKind kind)
    {
        if (connector.CanOpen(DbKind.Rank))
        {
            using var conn = connector.Open(DbKind.Rank);
            return RankDb.LoadAllSeasonRewards(conn, kind);
        }
        
        var list = new List<RankDb.RankSeasonRewardEntity>();
        
        if (!_rewards.TryGetValue(kind, out var dict) || dict.Count == 0)
            return list;

        foreach (var kvp in dict)
        {
            list.AddRange(kvp.Value.Select(x => x.Value));
        }
        
        return list;
    }

    public void UpsertRankSeason(RankDb.RankSeasonEntity season)
    {
        if (connector.CanOpen(DbKind.Rank))
        {
            using var conn = connector.Open(DbKind.Rank);
            RankDb.UpsertSeason(conn, season);
            return;
        }

        if (!_seasons.TryGetValue((SeasonKind)season.Kind, out var dict))
        {
            dict = new Dictionary<int, RankDb.RankSeasonEntity> { { season.SeasonNum, season } };
            _seasons[(SeasonKind)season.Kind] = dict;
            return;
        }
        
        dict[season.SeasonNum] = season;
    }

    public void EndRankSeason(SeasonKind kind, int seasonNum)
    {
        if (connector.CanOpen(DbKind.Rank))
        {
            using var conn = connector.Open(DbKind.Rank);
            RankDb.EndSeason(conn, kind, seasonNum);
            return;
        }

        if (!_seasons.TryGetValue(kind, out var bySeason) ||
            !bySeason.TryGetValue(seasonNum, out var season))
        {
            return;
        }
        
        season.State = (byte)SeasonState.Ended;
        if (_records.TryGetValue(kind, out var dict) &&
            dict.TryGetValue(season.SeasonNum, out var records))
        {
            records.Clear();
        }
    }

    public void UpsertRankSeasonRecord(RankDb.RankSeasonRecordEntity record)
    {
        if (connector.CanOpen(DbKind.Rank))
        {
            using var conn = connector.Open(DbKind.Rank);
            RankDb.UpsertSeasonRecord(conn, record);
            return;
        }

        if (!_records.TryGetValue((SeasonKind)record.SeasonKind, out var dict))
        {
            dict = new Dictionary<int, Dictionary<long, RankDb.RankSeasonRecordEntity>>();
            _records[(SeasonKind)record.SeasonKind] = dict;
        }
        
        if (!dict.TryGetValue(record.SeasonNum, out var records))
            dict[record.SeasonNum] = records = new Dictionary<long, RankDb.RankSeasonRecordEntity>();
        
        records[record.Uid] = record;
    }

    public void InsertRankSeasonReward(RankDb.RankSeasonRewardEntity reward)
    {
        if (connector.CanOpen(DbKind.Rank))
        {
            using var conn = connector.Open(DbKind.Rank);
            RankDb.InsertSeasonReward(conn, reward);
            return;
        }
        
        if (!_rewards.TryGetValue((SeasonKind)reward.SeasonKind, out var dict))
        {
            dict = new Dictionary<int, Dictionary<long, RankDb.RankSeasonRewardEntity>>();
            _rewards[(SeasonKind)reward.SeasonKind] = dict;
        }
        
        if (!dict.TryGetValue(reward.SeasonNum, out var rewards))
            dict[reward.SeasonNum] = rewards = new Dictionary<long, RankDb.RankSeasonRewardEntity>();
        
        rewards[reward.Uid] = reward;
    }

    public void ReceiveRankSeasonReward(SeasonKind kind, int seasonNum, long uid)
    {
        if (connector.CanOpen(DbKind.Rank))
        {
            using var conn = connector.Open(DbKind.Rank);
            RankDb.ReceiveSeasonReward(conn, kind, seasonNum, uid);
            return;
        }

        if (!_rewards.TryGetValue(kind, out var dict) ||
            !dict.TryGetValue(seasonNum, out var rewards) ||
            !rewards.TryGetValue(uid, out var reward))
        {
            return;
        }
        
        reward.IsRewarded = true;
    }
}
