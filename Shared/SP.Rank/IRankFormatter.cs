namespace SP.Rank;

public interface IRankFormatter<in TEntry, out TInfo>
    where TEntry : IRankEntry
{
    TInfo Format(TEntry entry, int rank);
}
