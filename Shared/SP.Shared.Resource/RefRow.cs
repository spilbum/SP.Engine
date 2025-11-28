namespace SP.Shared.Resource;

public sealed class RefRow(int count)
{
    private object?[] _values { get; } = new object[count];

    public object? Get(int index)
        => _values.Length <= index ? null : _values[index];

    public void Set(int index, object? value)
    {
        if (_values.Length > index)
            _values[index] = value;
    }
    
    public T? Get<T>(int index) 
        => _values.Length <= index ? default : (T)_values[index]!;
}
