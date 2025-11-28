namespace OperationTool.Diff.Models;

public sealed class CellDiffModel(RowDiffKind rowKind, RefsCellDiff inner)
{
    public string ColumnName => inner.ColumnName;
    public object? OldValue => inner.OldValue;
    public object? NewValue => inner.NewValue;
    public string OldText => OldValue?.ToString() ?? string.Empty;
    public string NewText => NewValue?.ToString() ?? string.Empty;

    public bool HasOld => inner.OldValue is not null;
    public bool HasNew => inner.NewValue is not null;
    
    public bool IsChanged =>
        rowKind == RowDiffKind.Modified
        || rowKind == RowDiffKind.Added && HasNew
        || rowKind == RowDiffKind.Removed && HasOld;
}
