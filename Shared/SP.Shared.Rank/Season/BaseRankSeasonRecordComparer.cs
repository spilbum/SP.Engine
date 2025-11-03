namespace SP.Shared.Rank.Season;

public abstract class BaseRankSeasonRecordComparer<TRecord> : IComparer<TRecord>
    where TRecord : IRankRecord
{
    public abstract int Compare(TRecord? x, TRecord? y);
}

public class ReverseComparer<T>(IComparer<T> inner) : IComparer<T>
{
    public int Compare(T? x, T? y)
    {
        return inner.Compare(y, x);
    }
}
