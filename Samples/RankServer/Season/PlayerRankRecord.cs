using SP.Shared.Rank.Season;

namespace RankServer.Season;

public sealed class PlayerRankRecord : BaseRankSeasonRecord<long>
{
    public PlayerRankRecord(long uid, int score, string? name, string? countryCode, DateTimeOffset? updateUtc = null)
        : base(uid)
    {
        Score = score;
        Name = name ?? "none";
        CountryCode = countryCode ?? "--";
        UpdateUtc = updateUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
    }

    public long Uid => Key;
    public int Score { get; }
    public string Name { get; }
    public string CountryCode { get; }
    public DateTimeOffset UpdateUtc { get; }

    public PlayerRankRecord WithScore(int newScore, string? name, string? countryCode, DateTimeOffset? updateUtc)
    {
        return new PlayerRankRecord(Uid, newScore, name, countryCode, updateUtc ?? DateTimeOffset.UtcNow);
    }
}
