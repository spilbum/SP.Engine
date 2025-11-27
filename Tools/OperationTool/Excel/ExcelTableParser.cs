using System.Globalization;
using ClosedXML.Excel;
using SP.Shared.Resource;

namespace OperationTool.Excel;

public static class ExcelTableParser
{
    public static List<ExcelTable> LoadWorkbook(string path)
    {
        using var wb = new XLWorkbook(path);
        
        return wb.Worksheets
            .Select(ParseSheet)
            .OfType<ExcelTable>()
            .ToList();
    }

    private static ExcelTable? ParseSheet(IXLWorksheet ws)
    {
        var used = ws.RangeUsed();
        if (used == null)
            return null;

        var firstRow = used.FirstRowUsed().RowNumber();
        var lastRow = used.LastRowUsed().RowNumber();
        var firstCol = used.FirstColumnUsed().ColumnNumber();
        var lastCol = used.LastColumnUsed().ColumnNumber();
        
        if (lastRow - firstRow + 1 < 4)
            return null;

        var sheetName = ws.Name;
        var table = new ExcelTable(sheetName);

        var headerRow = ws.Row(firstRow);
        var typeRow = ws.Row(firstRow + 1);
        var pkRow = ws.Row(firstRow + 2);

        var columns = new List<ExcelColumn>();
        
        for (var col = firstCol; col <= lastCol; col++)
        {
            var nameCell = headerRow.Cell(col);
            var name = nameCell.GetString().Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var typeStr = typeRow.Cell(col).GetString().Trim();
            if (string.IsNullOrEmpty(typeStr))
                typeStr = "string";

            var pkMark = pkRow.Cell(col).GetString().Trim();
            var isPk = !string.IsNullOrEmpty(pkMark) &&
                       (pkMark.Equals("key", StringComparison.OrdinalIgnoreCase) ||
                       pkMark.Equals("pk", StringComparison.OrdinalIgnoreCase));

            var colType = ParseColumnType(typeStr);
            var column = new ExcelColumn(col, name, colType, isPk);
            columns.Add(column);
        }
        
        if (columns.Count == 0)
            return null;
        
        foreach (var c in columns)
            table.Columns.Add(c);
        
        table.Rows.AddRange(
            ReadDataRows(ws, columns, firstRow + 3, lastRow, firstCol, lastCol)
        );
        
        return table;
    }
    
    private static List<ExcelRow> ReadDataRows(
        IXLWorksheet ws,
        List<ExcelColumn> columns,
        int firstRow,
        int lastRow,
        int firstCol,
        int lastCol)
    {
        var result = new List<ExcelRow>();

        for (var rowNum = firstRow; rowNum <= lastRow; rowNum++)
        {
            var excelRowRaw = ws.Row(rowNum);
            if (IsRowEmpty(excelRowRaw, firstCol, lastCol))
                continue;

            var row = new ExcelRow();
            foreach (var column in columns)
            {
                // column.Index 는 실제 엑셀 컬럼 인덱스(1-based)
                var cell = excelRowRaw.Cell(column.Index);
                var value = ConvertCellValue(cell, column.Type);
                row.Cells.Add(new ExcelCell(column, value));
            }

            result.Add(row);
        }

        return result;
    }

    private static bool IsRowEmpty(IXLRow row, int firstCol, int lastCol)
    {
        for (var col = firstCol; col <= lastCol; col++)
        {
            if (!row.Cell(col).IsEmpty())
                return false;
        }
        return true;
    }

    private static ColumnType ParseColumnType(string type)
    {
        return type.Trim().ToLowerInvariant() switch
        {
            "text" or "string" => ColumnType.String,
            "int" or "int32" => ColumnType.Int32,
            "long" or "int64" => ColumnType.Int64,
            "float" => ColumnType.Float,
            "double" => ColumnType.Double,
            "bool" or "boolean" => ColumnType.Boolean,
            "date" or "datetime" => ColumnType.DateTime,
            _ => ColumnType.String
        };
    }
   
    private static object? ConvertCellValue(IXLCell cell, ColumnType type)
    {
        if (cell.IsEmpty())
            return null;

        var s = cell.GetValue<string>()?.Trim();
        if (string.IsNullOrEmpty(s))
            return null;

        switch (type)
        {
            case ColumnType.String:
                return s;

            case ColumnType.Int32:
                if (!int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var i))
                    throw new FormatException($"Invalid {type} value '{s}' at {cell.Address}");
                return i;

            case ColumnType.Int64:
                if (!long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var l))
                    throw new FormatException($"Invalid {type} value '{s}' at {cell.Address}");
                return l;

            case ColumnType.Float:
                if (!float.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var f))
                    throw new FormatException($"Invalid {type} value '{s}' at {cell.Address}");
                return f;

            case ColumnType.Double:
                if (!double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                    throw new FormatException($"Invalid {type} value '{s}' at {cell.Address}");
                return d;

            case ColumnType.Boolean:
                if (bool.TryParse(s, out var b))
                    return b;
                if (int.TryParse(s, out var bi))
                    return bi != 0;
                throw new FormatException($"Invalid {type} value '{s}' at {cell.Address}");

            case ColumnType.DateTime:
                if (cell.DataType == XLDataType.DateTime)
                    return cell.GetDateTime();
                if (DateTime.TryParse(
                        s,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal,
                        out var dt))
                    return dt;
                throw new FormatException($"Invalid {type} value '{s}' at {cell.Address}");

            default:
                return s;
        }
    }
}


    


