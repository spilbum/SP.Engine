using System.Collections;
using System.Data;
using System.Data.Common;
using SP.Common.Accessor;

namespace SP.Database;

public interface IDatabaseEngineAdapter
{
    bool SupportsListParameter { get; }
    DbParameter AddWithList(DbCommand cmd, string name, IList list, Type elementType);
}

public class SqlServerEngineAdapter : IDatabaseEngineAdapter
{
    public bool SupportsListParameter => true;
    public DbParameter AddWithList(DbCommand cmd, string name, IList list, Type elementType)
    {
        if (cmd is not Microsoft.Data.SqlClient.SqlCommand sc)
            throw new InvalidOperationException("SQL Server command required.");

        var accessor = RuntimeTypeAccessor.GetOrCreate(elementType);
        var dataTable = new DataTable();

        foreach (var member in accessor.Members)
            dataTable.Columns.Add(member.Name, member.Type).AllowDBNull = member.IsNullable();

        foreach (var item in list)
        {
            var row = dataTable.NewRow();
            foreach (var member in accessor.Members)
                row[member.Name] = accessor[item, member.Name] ?? DBNull.Value;
            dataTable.Rows.Add(row);
        }
        
        var param = sc.CreateParameter();
        param.ParameterName = name;
        param.SqlDbType = SqlDbType.Structured;
        param.Value = dataTable;
        sc.Parameters.Add(param);
        return param;
    }
}
