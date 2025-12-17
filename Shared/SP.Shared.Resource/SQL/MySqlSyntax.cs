using System;
using SP.Shared.Resource.Table;

namespace SP.Shared.Resource.SQL;

public sealed class MySqlSyntax : ISqlSyntax
{
    public string QuoteIdentifier(string name)
    {
        var escaped = name.Replace("`", "``");
        return $"`{escaped}`";
    }

    public string GetColumnType(RefColumn column)
    {
        return column.Type switch
        {
            ColumnType.String => column.Length is > 0 ? $"VARCHAR({column.Length})" : "TEXT",
            ColumnType.Byte => "TINYINT UNSIGNED",
            ColumnType.Int32 => "INT",
            ColumnType.Int64 => "BIGINT",
            ColumnType.Float => "FLOAT",
            ColumnType.Double => "DOUBLE",
            ColumnType.Boolean => "TINYINT(1)",
            ColumnType.DateTime => "DATETIME",
            _ => throw new NotSupportedException($"Unsupported type: {column.Type}")
        };
    }
    
    public string FormatValue(RefColumn column, object? value)
    {
        if (value is null)
            return "NULL";

        if (column.Type != ColumnType.String)
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "NULL";
        
        var s = value.ToString() ?? string.Empty;
        s = s.Replace("'", "''");
        return $"'{s}'";

    }
    
    public string GetDropTableIfExistsSql(string tableName)
        => $"DROP TABLE IF EXISTS {QuoteIdentifier(tableName)};";
}
