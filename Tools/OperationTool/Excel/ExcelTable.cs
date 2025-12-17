namespace OperationTool.Excel;

public sealed class ExcelTable(string name)
{
    public string Name { get; } = name;
    public List<ExcelColumn> Columns { get; } = [];
    public List<ExcelRow> Rows { get; } = [];
}
