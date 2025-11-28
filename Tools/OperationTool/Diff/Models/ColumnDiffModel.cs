using SP.Shared.Resource;

namespace OperationTool.Diff.Models;

public sealed class ColumnDiffModel(RefsColumnDiff inner)
{
    public string Name => inner.Name;
    public ColumnDiffKind Kind => inner.Kind;
    public ColumnType? OldType => inner.OldType;
    public ColumnType? NewType => inner.NewType;
    public bool? OldIsKey => inner.OldIsKey;
    public bool? NewIsKey => inner.NewIsKey;

    public string TypeChangeText
        => Kind switch
        {
            ColumnDiffKind.Added => NewType is null ? "" : $"Type: {NewType}",
            ColumnDiffKind.Removed => OldType is null ? "" : $"Type: {OldType}",
            ColumnDiffKind.Modified => (OldType, NewType) switch
            {
                (not null, not null) when OldType != NewType => $"Type: {OldType} -> {NewType}",
                _ => ""
            },
            _ => ""
        };
    
    public bool HasKeyChange =>
        Kind == ColumnDiffKind.Modified &&
        OldIsKey.HasValue && NewIsKey.HasValue &&
        OldIsKey.Value != NewIsKey.Value;

    public string KeyChangeText
        => HasKeyChange
            ? $"Key: {OldIsKey} -> {NewIsKey}"
            : "";
}
