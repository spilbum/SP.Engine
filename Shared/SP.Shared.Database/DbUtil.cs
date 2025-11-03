using System.Data;
using SP.Core.Accessor;

namespace SP.Shared.Database;

public static class DbUtil
{
    public static DataTable ToDataTable<T>(IEnumerable<T> list) where T : BaseDbEntity
    {
        var elementType = typeof(T);
        var accessor = RuntimeTypeAccessor.GetOrCreate(elementType);
        var dataTable = new DataTable(accessor.Name);

        foreach (var member in accessor.Members)
            dataTable.Columns.Add(member.Name, member.Type).AllowDBNull = member.IsNullable();

        foreach (var item in list)
        {
            var row = dataTable.NewRow();
            foreach (var m in accessor.Members)
            {
                if (!m.CanGet || m.IgnoreGet) continue;

                var val = m.GetValue(item);
                row[m.Name] = val ?? DBNull.Value;
            }

            dataTable.Rows.Add(row);
        }

        return dataTable;
    }
}
