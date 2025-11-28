using System.Collections.Generic;

namespace SP.Shared.Resource;

public sealed class RefTableData(string name)
{
    public string Name { get; } = name;
    public List<RefRow> Rows { get; } = [];
}
