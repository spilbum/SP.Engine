using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SP.Shared.Resource.SQL;
using SP.Shared.Resource.Table;

namespace SP.Shared.Resource;

public static class TableSqlBuilder
{
    public static string BuildCreateTableSql(
        RefTableSchema schema, 
        ISqlSyntax syntax)
    {
        if (schema.Columns.Count == 0)
            throw new ArgumentException("Schema must contain at least one column.", nameof(schema));
        
        var sb = new StringBuilder();
        sb.AppendLine(syntax.GetDropTableIfExistsSql(schema.Name));
        sb.AppendLine();
        sb.AppendLine($"CREATE TABLE {syntax.QuoteIdentifier(schema.Name)} (");

        var columnDefs = (
            from column in schema.Columns
            let type = syntax.GetColumnType(column)
            select $"   {syntax.QuoteIdentifier(column.Name)} {type} NOT NULL"
        ).ToList();

        var keys = schema.Columns
            .Where(column => column.IsKey)
            .Select(column => syntax.QuoteIdentifier(column.Name))
            .ToArray();

        if (keys.Length > 0)
        {
            columnDefs.Add($"   PRIMARY KEY ({string.Join(", ", keys)})");
        }

        sb.AppendLine(string.Join(",\n", columnDefs));
        sb.AppendLine(");");
        
        return sb.ToString();
    }

    public static IEnumerable<string> BuildBatchInsertSql(
        RefTableSchema schema,
        RefTableData data,
        ISqlSyntax syntax,
        int batchSize = 500,
        int maxSqlLength = -1)
    {
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize), "batchSize must be greater than zero.");
        
        if (data.Rows.Count == 0)
            yield break;
        
        var columns = schema.Columns;
        var colNames = schema.Columns
            .Select(column => syntax.QuoteIdentifier(column.Name))
            .ToArray();
        
        var header = 
            $"INSERT INTO {syntax.QuoteIdentifier(schema.Name)} " +
            $"({string.Join(", ", colNames)}) VALUES ";
        
        var sb = new StringBuilder();
        var counter = 0;

        foreach (var row in data.Rows)
        {
            var values = new string[columns.Count];

            for (var i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                var val = row.Get(i);
                values[i] = syntax.FormatValue(col, val);
            }

            var valueClause = $"({string.Join(", ", values)})";

            if (counter == 0)
            {
                sb.Clear();
                sb.Append(header);
            }
            else
            {
                sb.Append(", ");
            }

            sb.Append(valueClause);
            counter++;

            var exceeded = maxSqlLength > 0 && sb.Length >= maxSqlLength;
            
            if (counter < batchSize && !exceeded) 
                continue;
            
            sb.Append(";");
            yield return sb.ToString();
            counter = 0;
        }

        if (counter <= 0) 
            yield break;
        
        sb.Append(";");
        yield return sb.ToString();
    }
}
