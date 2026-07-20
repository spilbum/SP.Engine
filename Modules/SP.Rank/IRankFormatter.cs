namespace SP.Rank;

public interface IRankFormatter<in TKey, in TRecord, out TInfo>
    where TKey : IComparable<TKey>
    where TRecord : IRankRecord<TKey>
{
    TInfo Format(TRecord record, int rank);
}
