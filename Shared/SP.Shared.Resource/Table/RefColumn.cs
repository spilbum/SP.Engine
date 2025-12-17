namespace SP.Shared.Resource.Table;

public sealed class RefColumn(string name, ColumnType type, bool isKey, int? length = null)
{
    public string Name { get; } = name;
    public ColumnType Type { get; } = type;
    public bool IsKey { get; } = isKey;
    public int? Length { get; } = length;
}
