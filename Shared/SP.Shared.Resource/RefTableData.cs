using System.Collections.Generic;

namespace SP.Shared.Resource;

public sealed class RefRow(int count)
{
    private object?[] _values { get; } = new object[count];

    public object? Get(int index) => _values[index];
    public void Set(int index, object? value) => _values[index] = value;
    
    public T Get<T>(int index) => (T)_values[index]!;
}

public sealed class RefTableData(string name)
{
    public string Name { get; } = name;
    public List<RefRow> Rows { get; } = [];
}
