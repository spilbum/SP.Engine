namespace SP.Rank.Season;

public abstract class RankSeasonEntry(long key) : IRankEntry<long>
{
    public long Key { get; } = key;
}

public abstract class RankSeasonEntryComparer<TEntry> : IRankEntryComparer<TEntry>
    where TEntry : RankSeasonEntry
{
    public abstract int Compare(TEntry? x, TEntry? y);
}
