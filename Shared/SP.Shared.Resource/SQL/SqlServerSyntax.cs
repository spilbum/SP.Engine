using System;
using SP.Shared.Resource.Table;

namespace SP.Shared.Resource.SQL;

public sealed class SqlServerSyntax : ISqlSyntax
{
    public string QuoteIdentifier(string name)
    {
        var escaped = name.Replace("]", "]]");
        return $"[{escaped}]";
    }

    public string GetColumnType(RefColumn column)
    {
        return column.Type switch
        {
            ColumnType.String => column.Length is > 0 ? $"NVARCHAR({column.Length})" : "NVARCHAR(MAX)",
            ColumnType.Byte => "TINYINT",
            ColumnType.Int32 => "INT",
            ColumnType.Int64 => "BIGINT",
            ColumnType.Float => "REAL",
            ColumnType.Double => "FLOAT",
            ColumnType.Boolean => "BIT",
            ColumnType.DateTime => "DATETIME2",
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
        return $"N'{s}'";
    }

    public string GetDropTableIfExistsSql(string tableName)
        => $"""
            IF OBJECT_ID('{tableName}', 'U') IS NOT NULL
                DROP TABLE {QuoteIdentifier(tableName)};
            """;
}

