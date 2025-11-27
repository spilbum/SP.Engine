namespace OperationTool.Excel;

public sealed class ExcelCell(ExcelColumn column, object? value)
{
    public ExcelColumn Column { get; } = column;
    public object? Value { get; } = value;
}
