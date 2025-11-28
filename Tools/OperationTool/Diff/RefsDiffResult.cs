using SP.Shared.Resource;

namespace OperationTool.Diff;

public enum TableDiffKind
{
    Unchanged,
    Added,
    Removed,
    Modified
}

public enum RowDiffKind
{
    Added,
    Removed,
    Modified
}

public enum ColumnDiffKind
{
    Added,
    Removed,
    Modified
}

public class RefsDiffResult
{
    public List<RefsTableDiff> Tables { get; } = [];
}

public sealed class RefsTableDiff
{
    public string Name { get; init; } = "";
    public TableDiffKind Kind { get; set; } = TableDiffKind.Unchanged;

    public List<RefsColumnDiff> Columns { get; } = [];
    public List<RefsRowDiff> Rows { get; } = [];
    
    public bool PrimaryKeyChanged { get; set; }
}

public sealed class RefsColumnDiff
{
    public string Name { get; init; } = "";
    public ColumnDiffKind Kind { get; init; }
    
    public ColumnType? OldType { get; init; }
    public ColumnType? NewType { get; init; }
    
    public bool? OldIsKey { get; init; }
    public bool? NewIsKey { get; init; }
}

public sealed class RefsRowDiff
{
    public string Key { get; init; } = "";
    public RowDiffKind Kind { get; init; }

    public List<RefsCellDiff> Cells { get; init; } = [];
}

public sealed class RefsCellDiff
{
    public string ColumnName { get; init; } = "";
    public object? OldValue { get; init; }
    public object? NewValue { get; init; }
}
