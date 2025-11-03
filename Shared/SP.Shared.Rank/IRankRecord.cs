namespace SP.Shared.Rank;

public interface IRankRecord
{
}

public interface IRankRecord<out TKey> : IRankRecord where TKey : notnull
{
    TKey Key { get; }
}
