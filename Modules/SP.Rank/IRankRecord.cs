namespace SP.Rank;

public interface IRankRecord<out TKey>
    where TKey : notnull
{
    TKey Key { get; }
    long Time { get; }
}
