namespace SP.Shared.Rank;

public interface IRankFormatter<in TRecord, out TInfo>
    where TRecord : IRankRecord
{
    TInfo Format(TRecord record, int rank);
}
