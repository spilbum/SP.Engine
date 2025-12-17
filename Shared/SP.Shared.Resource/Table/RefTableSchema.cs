using System;
using System.Collections.Generic;

namespace SP.Shared.Resource.Table;

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


