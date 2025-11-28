using System.Collections.ObjectModel;
using OperationTool.ViewModels;

namespace OperationTool.Diff.Models;

public sealed class TableDiffModel : ViewModelBase
{
    private readonly RefsTableDiff _inner;
    private RowDiffModel? _selectedRow;

    public RowDiffModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
                UpdateSelectedRowCells();
        }
    }
    
    public string Name => _inner.Name;
    public TableDiffKind Kind => _inner.Kind;
    public bool PrimaryKeyChanged => _inner.PrimaryKeyChanged;
    public int ColumnDiffCount => Columns.Count;
    public int RowDiffCount => Rows.Count;

    public ObservableCollection<CellDiffModel> SelectedRowCells { get; } = [];
    public ObservableCollection<ColumnDiffModel> Columns { get; }
    public ObservableCollection<RowDiffModel> Rows { get; }
    
    public TableDiffModel(RefsTableDiff inner)
    {
        _inner = inner;

        Columns = new ObservableCollection<ColumnDiffModel>(
            inner.Columns.Select(column => new ColumnDiffModel(column)));
        
        Rows = new ObservableCollection<RowDiffModel>(
            inner.Rows.Select(row => new RowDiffModel(row)));
    }
    
    private void UpdateSelectedRowCells()
    {
        SelectedRowCells.Clear();
        
        if (_selectedRow == null)
            return;

        foreach (var cell in _selectedRow.Cells)
        {
            SelectedRowCells.Add(cell);
        }
        
        OnPropertyChanged(nameof(SelectedRowCells));
    }
}





