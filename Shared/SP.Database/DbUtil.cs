using System.Data;
using SP.Common.Accessor;

namespace SP.Database;

public static class DbUtil
{
    public static DataTable ToDataTable<T>(IEnumerable<T> list) where T : BaseDbRecord
    {
        var elementType = typeof(T);
        var accessor = RuntimeTypeAccessor.GetOrCreate(elementType);
        var dataTable = new DataTable(accessor.Name);

        foreach (var member in accessor.Members)
            dataTable.Columns.Add(member.Name, member.Type).AllowDBNull = member.IsNullable();

        foreach (var item in list)
        {
            var row = dataTable.NewRow();
            foreach (var member in accessor.Members)
                row[member.Name] = accessor[item, member.Name] ?? DBNull.Value;
            dataTable.Rows.Add(row);
        }
        
        return dataTable;
    }
}
