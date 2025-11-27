using OperationTool.Excel;
using OperationTool.ViewModels;
using SP.Shared.Resource;

namespace OperationTool.Models;

public sealed class ExcelTableModel : ViewModelBase
{
    private readonly ExcelTable _excelTable;
    private string _name = string.Empty;
    private bool _checked;

    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    public bool IsChecked
    {
        get => _checked;
        set => SetProperty(ref _checked, value);
    }
    
    public ExcelTableModel(ExcelTable table)
    {
        Name = table.Name;
        _excelTable = table;
    }

    public RefTableSchema GetSchema()
    {
        var schema = new RefTableSchema(Name);
        schema.Columns.AddRange(_excelTable.Columns.Select(column => new RefColumn(column.Name, column.Type, column.IsKey)));
        return schema;
    }

    public RefTableData GetData()
    {
        var data = new RefTableData(Name);
        
        foreach (var r in _excelTable.Rows)
        {
            var row = new RefRow(r.Cells.Count);
            for (var i = 0; i < r.Cells.Count; i++)
            {
                var value = r.Cells[i].Value;
                row.Set(i, value);
            }
            data.Rows.Add(row);
        }
        
        return data;
    }
}
