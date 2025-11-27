using System;
using System.Collections.Generic;

namespace SP.Shared.Resource;

public sealed class RefTableSchema(string name)
{
    public string Name { get; } = name;
    public List<RefColumn> Columns { get; } = [];

    public int GetColumnIndex(string name)
    {
        for (var i = 0; i < Columns.Count; i++)
        {
            if (string.Equals(Columns[i].Name, name, StringComparison.Ordinal))
                return i;
        }

        throw new InvalidOperationException($"Column not found: {name} (table={name})");
    }
}

public sealed class RefColumn(string name, ColumnType type, bool isKey)
{
    public string Name { get; } = name;
    public ColumnType Type { get; } = type;
    public bool IsKey { get; } = isKey;
}
