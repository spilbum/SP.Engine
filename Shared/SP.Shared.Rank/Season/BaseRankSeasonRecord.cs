namespace SP.Shared.Rank.Season;

public abstract class BaseRankSeasonRecord<TKey>(TKey key) : IRankRecord<TKey>
    where TKey : notnull
{
    public TKey Key { get; } = key;
}
