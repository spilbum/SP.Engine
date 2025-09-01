
namespace SP.Rank;

public interface IRankEntry
{
    
}

public interface IRankEntry<out TKey> : IRankEntry where TKey : notnull
{ 
    TKey Key { get; }
}

public interface IRankEntryComparer<in TEntry> : IComparer<TEntry>
    where TEntry : IRankEntry
{
    
}
