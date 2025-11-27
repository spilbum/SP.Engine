using SP.Shared.Resource;

namespace OperationTool.Excel;

public sealed class ExcelColumn(int index, string name, ColumnType type, bool isKey)
{
    public int Index { get; } = index;
    public string Name { get; } = name;
    public ColumnType Type { get; } = type;
    public bool IsKey { get; } = isKey;
}

