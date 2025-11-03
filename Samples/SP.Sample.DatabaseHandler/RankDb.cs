using System.Data;
using SP.Core.Accessor;
using SP.Sample.Common;
using SP.Shared.Database;

namespace SP.Sample.DatabaseHandler;

public static class RankDb
{
    public static RankSeasonEntity? LoadLatestSeason(DbConn conn, SeasonKind kind)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "Proc_Season_LoadLatest");
        cmd.Add("season_kind", DbType.Byte, kind);
        return cmd.ExecuteReader<RankSeasonEntity>();
    }

    public static RankSeasonEntity? LoadSeason(DbConn conn, SeasonKind kind, int seasonNum)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "Proc_Season_LoadSeason");
        cmd.Add("season_kind", DbType.Byte, kind);
        cmd.Add("season_num", DbType.Int32, seasonNum);
        return cmd.ExecuteReader<RankSeasonEntity>();
    }

    public static bool UpsertSeason(DbConn conn, RankSeasonEntity season)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "Proc_Season_Upsert");
        cmd.AddWithEntity(season);
        return cmd.ExecuteNonQuery() > 0;
    }

    public static void EndSeason(DbConn conn, SeasonKind kind, int seasonNum)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "Proc_Season_EndSeason");
        cmd.Add("season_kind", DbType.Byte, kind);
        cmd.Add("season_num", DbType.Int32, seasonNum);
        cmd.ExecuteNonQuery();
    }

    public static List<RankSeasonRecordEntity> LoadAllSeasonRecords(DbConn conn, SeasonKind kind, int seasonNum)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "Proc_Season_LoadAllRecords");
        cmd.Add("season_kind", DbType.Byte, kind);
        cmd.Add("season_num", DbType.Int32, seasonNum);
        return cmd.ExecuteReaderList<RankSeasonRecordEntity>();
    }

    public static RankSeasonRecordEntity? LoadSeasonRecord(DbConn conn, SeasonKind kind, int seasonNum, long uid)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "Proc_Season_LoadRecord");
        cmd.Add("season_kind", DbType.Byte, kind);
        cmd.Add("season_num", DbType.Int32, seasonNum);
        cmd.Add("uid", DbType.Int64, uid);
        return cmd.ExecuteReader<RankSeasonRecordEntity>();
    }

    public static void UpsertSeasonRecord(DbConn conn, RankSeasonRecordEntity record)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "Proc_Season_UpsertRecord");
        cmd.AddWithEntity(record);
        cmd.ExecuteNonQuery();
    }
    
    public static List<RankSeasonRewardEntity> LoadAllSeasonRewards(DbConn conn, SeasonKind kind)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "Proc_Season_LoadAllRewards");
        cmd.Add("season_kind", DbType.Byte, kind);
        return cmd.ExecuteReaderList<RankSeasonRewardEntity>();
    }

    public static void InsertSeasonReward(DbConn conn, RankSeasonRewardEntity reward)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "Proc_Season_InsertReward");
        cmd.AddWithEntity(reward);
        cmd.ExecuteNonQuery();
    }

    public static void ReceiveSeasonReward(DbConn conn, SeasonKind kind, int seasonNum, long uid)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "Proc_Season_ReceiveReward");
        cmd.Add("season_kind", DbType.Byte, kind);
        cmd.Add("season_num", DbType.Int32, seasonNum);
        cmd.Add("uid", DbType.Int64, uid);
        cmd.ExecuteNonQuery();
    }

    public class RankSeasonEntity : BaseDbEntity
    {
        [Member("end_utc")] public DateTime EndUtc;
        [Member("season_kind")] public byte Kind;
        [Member("season_num")] public int SeasonNum;
        [Member("start_utc")] public DateTime StartUtc;
        [Member("state")] public byte State;
    }

    public class RankSeasonRecordEntity : BaseDbEntity
    {
        [Member("country_code")] public string? CountryCode;
        [Member("name")] public string? Name;
        [Member("score")] public int Score;
        [Member("season_kind")] public byte SeasonKind;
        [Member("season_num")] public int SeasonNum;
        [Member("uid")] public long Uid;
        [Member("updated_utc")] public DateTime UpdatedUtc;
    }

    public class RankSeasonRewardEntity : BaseDbEntity
    {
        [Member("is_rewarded")] public bool IsRewarded;
        [Member("item_id")] public int ItemId;
        [Member("item_kind")] public byte ItemKind;
        [Member("item_value")] public int ItemValue;
        [Member("season_rank")] public int Rank;
        [Member("score")] public int Score;
        [Member("season_kind")] public byte SeasonKind;
        [Member("season_num")] public int SeasonNum;
        [Member("uid")] public long Uid;
    }
}
