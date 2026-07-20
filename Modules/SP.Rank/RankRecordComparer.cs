namespace SP.Rank;

public abstract class RankRecordComparer<TKey, TRecord> : IComparer<TRecord>
    where TKey : IComparable<TKey>
    where TRecord : IRankRecord<TKey>
{
    protected abstract int CompareValue(TRecord x, TRecord y);

    public int Compare(TRecord? x, TRecord? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return 1;
        if (y is null) return -1;

        var result = CompareValue(x, y);
        if (result != 0)
            return result;

        var keyResult = x.Key.CompareTo(y.Key);
        return keyResult != 0 ? keyResult : 0;
    }
}

