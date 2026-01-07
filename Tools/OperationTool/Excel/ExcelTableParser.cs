using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using SP.Shared.Resource;
using UIKit;

namespace OperationTool.Excel;

public static partial class ExcelTableParser
{
    public static List<ExcelTable> LoadWorkbook(string path)
    {
        using var wb = new XLWorkbook(path);
        
       var result = new List<ExcelTable>();
       foreach (var ws in wb.Worksheets)
       {
           SheetGrid grid;
           try
           {
               grid = XlsxGridReader.ReadSheet(ws);
           }
           catch
           {
               continue;
           }

           var table = ParseGrid(ws.Name, grid);
           if (table != null)
               result.Add(table);
       }
       
       return result;
    }

    private static ExcelTable? ParseGrid(string sheetName, SheetGrid grid)
    {
        if (grid.RowCount < 4)
            return null;

        const int headerRow = 0;
        const int typeRow = 1;
        const int pkRow = 2;
        const int dataStartRow = 3;

        var table = new ExcelTable(sheetName);
        
        var columns = new List<ExcelColumn>();
        for (var col = 0; col < grid.ColumnCount; col++)
        {
            var name = grid.Get(headerRow, col).Trim();
            if (!ValidateColumnName(name))
                throw new InvalidDataException($"Invalid column name: {name} ({grid.Address(headerRow, col)})");

            var typeStr = grid.Get(typeRow, col).Trim();
            if (string.IsNullOrEmpty(typeStr))
                throw new InvalidDataException($"Column type is empty ({grid.Address(headerRow, col)})");

            var pkMark = grid.Get(pkRow, col).Trim();
            var isPk = !string.IsNullOrEmpty(pkMark) && pkMark.Equals("key", StringComparison.OrdinalIgnoreCase);

            var (colType, colLength) = ParseColumnType(typeStr);
            var column = new ExcelColumn(col, name, colType, isPk, colLength);
            columns.Add(column);
        }
        
        if (columns.Count == 0)
            return null;
        
        foreach (var c in columns)
            table.Columns.Add(c);
        
        table.Rows.AddRange(ReadDataRows(grid, columns, dataStartRow));
        
        return table;
    }

    private static bool ValidateColumnName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        
        // 숫자 시작 금지
        if (char.IsDigit(name[0]))
            return false;
        
        // 허용 문자: 알파벳, 숫자, 언더스코어
        return name.All(c => char.IsLetterOrDigit(c) || c == '_');
    }
    
    private static List<ExcelRow> ReadDataRows(
        SheetGrid grid,
        List<ExcelColumn> columns,
        int startRow)
    {
        var result = new List<ExcelRow>();

        for (var r = startRow; r < grid.RowCount; r++)
        {
            if (grid.IsRowEmpty(r))
                continue;
            
            var row = new ExcelRow();
            for (var c = 0; c < grid.ColumnCount; c++)
            {
                var column = columns[c];
                var raw = grid.Get(r, c);
                var value = ConvertCellValue(raw, column.Type, grid.Address(r, c));
                row.Cells.Add(new ExcelCell(column, value));
            }

            result.Add(row);
        }

        return result;
    }

    private static (ColumnType type, int? length) ParseColumnType(string input)
    {
        var (type, length) = ParseTypeWithLength(input);
        return type switch
        {
            "string" => (ColumnType.String, length),
            "byte" => (ColumnType.Byte, length),
            "int32" => (ColumnType.Int32, length),
            "int64" => (ColumnType.Int64, length),
            "float" => (ColumnType.Float, length),
            "double" => (ColumnType.Double, length),
            "boolean" => (ColumnType.Boolean, length),
            "datetime" => (ColumnType.DateTime, length),
            _ => throw new NotSupportedException($"Unsupported: {type}")
        };
    }
    
    private static (string type, int? length) ParseTypeWithLength(string input)
    {
        var match = ColumnTypeRegex().Match(input.Trim());
        if (!match.Success)
            throw new FormatException($"Invalid type format: {input}");

        var type = match.Groups["type"].Value.ToLowerInvariant();

        int? length = null;
        if (match.Groups["len"].Success)
            length = int.Parse(match.Groups["len"].Value);

        return (type, length);
    }
   
    private static object? ConvertCellValue(string raw, ColumnType type, string addr)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var s = raw.Trim();
        if (string.IsNullOrEmpty(s))
            return null;

        switch (type)
        {
            case ColumnType.String:
                return s;
            
            case ColumnType.Byte:
                if(!byte.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var bt))
                    throw new FormatException($"Invalid {type} value '{s}' at {addr}");
                return bt;

            case ColumnType.Int32:
                if (!int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var i))
                    throw new FormatException($"Invalid {type} value '{s}' at {addr}");
                return i;

            case ColumnType.Int64:
                if (!long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var l))
                    throw new FormatException($"Invalid {type} value '{s}' at {addr}");
                return l;

            case ColumnType.Float:
                if (!float.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var f))
                    throw new FormatException($"Invalid {type} value '{s}' at {addr}");
                return f;

            case ColumnType.Double:
                if (!double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                    throw new FormatException($"Invalid {type} value '{s}' at {addr}");
                return d;

            case ColumnType.Boolean:
                if (bool.TryParse(s, out var b))
                    return b;
                if (int.TryParse(s, out var bi))
                    return bi != 0;
                throw new FormatException($"Invalid {type} value '{s}' at {addr}");

            case ColumnType.DateTime:
                if (DateTime.TryParse(
                        s,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal,
                        out var dt))
                    return dt;
                throw new FormatException($"Invalid {type} value '{s}' at {addr}");

            default:
                return s;
        }
    }

    [GeneratedRegex(@"^(?<type>[a-zA-Z0-9]+)(\((?<len>\d+)\))?$", RegexOptions.IgnoreCase, "ko-KR")]
    private static partial Regex ColumnTypeRegex();
}

public sealed class SheetGrid(string[,] cells, int firstRow, int firstCol)
{
    public int FirstRow { get; } = firstRow;
    public int FirstCol { get; } = firstCol;
    public int RowCount { get; } = cells.GetLength(0);
    public int ColumnCount { get; } = cells.GetLength(1);

    public string Get(int r, int c)
    {
        if ((uint)r >= (uint)RowCount || (uint)c >= (uint)ColumnCount)
            return string.Empty;
        return cells[r, c];
    }

    public bool IsRowEmpty(int r)
    {
        for (var c = 0; c < ColumnCount; c++)
        {
            if (!string.IsNullOrWhiteSpace(Get(r, c)))
                return false;
        }
        return true;
    }

    public string Address(int r, int c)
        => $"R{FirstRow + r}C{FirstCol + c}";
}

public static class XlsxGridReader
{
    public static SheetGrid ReadSheet(IXLWorksheet ws)
    {
        var used = ws.RangeUsed() ?? throw new InvalidDataException($"Sheet '{ws.Name}' is empty.");
        
        var firstRow = used.FirstRowUsed().RowNumber();
        var lastRow = used.LastRowUsed().RowNumber();
        var firstCol = used.FirstColumnUsed().ColumnNumber();
        var lastCol = used.LastColumnUsed().ColumnNumber();

        var rowCount = lastRow - firstRow + 1;
        var columnCount = lastCol - firstCol + 1;

        var cells = new string[rowCount, columnCount];
        
        for (var r = 0; r < rowCount; r++)
        for (var c = 0; c < columnCount; c++)
        {
            var cell = ws.Row(firstRow + r).Cell(firstCol + c);
            cells[r, c] = cell.GetString()?.Trim() ?? string.Empty;
        }
        
        return new SheetGrid(cells, firstRow, firstCol);
    }
}


