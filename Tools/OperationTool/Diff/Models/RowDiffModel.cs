using System.Collections.ObjectModel;

namespace OperationTool.Diff.Models;

public sealed class RowDiffModel
{
    private readonly RefsRowDiff _inner;

    public RowDiffModel(RefsRowDiff inner)
    {
        _inner = inner;
        
        Cells = new ObservableCollection<CellDiffModel>(
            inner.Cells.Select(cell => new CellDiffModel(inner.Kind, cell)));
    }

    public string Key => _inner.Key;
    public RowDiffKind Kind => _inner.Kind;
    public int CellDiffCount => Cells.Count;
    public ObservableCollection<CellDiffModel> Cells { get; }
}
