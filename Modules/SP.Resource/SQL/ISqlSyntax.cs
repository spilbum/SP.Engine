
using SP.Resource.Table;

namespace SP.Resource.SQL;

public interface ISqlSyntax
{
    string QuoteIdentifier(string name);
    string GetColumnType(RefColumn column);
    string FormatValue(RefColumn column, object? value);
    string GetDropTableIfExistsSql(string tableName);
}






