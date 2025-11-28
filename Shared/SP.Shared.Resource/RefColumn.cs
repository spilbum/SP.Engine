namespace SP.Shared.Resource;

public sealed class RefColumn(string name, ColumnType type, bool isKey)
{
    public string Name { get; } = name;
    public ColumnType Type { get; } = type;
    public bool IsKey { get; } = isKey;
}
