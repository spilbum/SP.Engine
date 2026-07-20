namespace SP.Rank;

public abstract class RankRecordBase<TKey>(TKey key, long time) : IRankRecord<TKey>
    where TKey : notnull
{
    public TKey Key { get; } = key;
    public long Time { get; } = time;
}
