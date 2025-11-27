namespace OperationTool.Excel;

public static partial class ExcelExtensions
{
    public static void Validate(this ExcelTable table)
    {
        var pkCols = table.Columns.Where(x => x.IsKey).ToList();
        if (pkCols.Count == 0)
            throw new InvalidOperationException($"Table '{table.Name}' has no primary key.");

        var set = new HashSet<string>();
        foreach (var row in table.Rows)
        {
            var key = string.Join("|",
                row.Cells.Where(c => c.Column.IsKey).Select(c => c.Value ?? string.Empty));
            
            if (!set.Add(key))
                throw new InvalidOperationException($"Duplicate PK in table '{table.Name}': {key}.");
        }
    }
}

public sealed class ExcelTable(string name)
{
    public string Name { get; } = name;
    public List<ExcelColumn> Columns { get; } = [];
    public List<ExcelRow> Rows { get; } = [];
}
