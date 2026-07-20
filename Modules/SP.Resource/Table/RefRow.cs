using System;

using System.Runtime.CompilerServices;

namespace SP.Resource.Table;

public struct RefValue
{
    public long PrimitiveData;
    public string? StringData;
}

public sealed class RefRow(int count)
{
    private readonly RefValue[] _values = new RefValue[count];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureIndex(int index)
    {
        if ((uint)index >= (uint)_values.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
    }

    public void SetString(int index, string? value)
    {
        EnsureIndex(index);
        _values[index].StringData = value;
    }

    public void SetByte(int index, byte value)
    {
        EnsureIndex(index);
        _values[index].PrimitiveData = value;
    }

    public void SetInt32(int index, int value)
    {
        EnsureIndex(index);
        _values[index].PrimitiveData = value;
    }

    public void SetInt64(int index, long value)
    {
        EnsureIndex(index);
        _values[index].PrimitiveData = value;
    }

    public void SetSingle(int index, float value)
    {
        EnsureIndex(index);
        _values[index].PrimitiveData = BitConverter.SingleToInt32Bits(value);
    }

    public void SetDouble(int index, double value)
    {
        EnsureIndex(index);
        _values[index].PrimitiveData = BitConverter.DoubleToInt64Bits(value);
    }

    public void SetBoolean(int index, bool value)
    {
        EnsureIndex(index);
        _values[index].PrimitiveData = value ? 1L : 0L;
    }

    public void SetDateTime(int index, DateTime value)
    {
        EnsureIndex(index);
        _values[index].PrimitiveData = value.Ticks;
    }

    public string GetString(int index)
    {
        EnsureIndex(index);
        return _values[index].StringData ?? string.Empty;
    }

    public byte GetByte(int index)
    {
        EnsureIndex(index);
        return (byte)_values[index].PrimitiveData;
    }

    public int GetInt32(int index)
    {
        EnsureIndex(index);
        return (int)_values[index].PrimitiveData;
    }

    public long GetInt64(int index)
    {
        EnsureIndex(index);
        return _values[index].PrimitiveData;
    }

    public float GetSingle(int index)
    {
        EnsureIndex(index);
        return BitConverter.Int32BitsToSingle((int)_values[index].PrimitiveData);
    }

    public double GetDouble(int index)
    {
        EnsureIndex(index);
        return BitConverter.Int64BitsToDouble(_values[index].PrimitiveData);
    }

    public bool GetBoolean(int index)
    {
        EnsureIndex(index);
        return _values[index].PrimitiveData != 0;
    }

    public DateTime GetDateTime(int index)
    {
        EnsureIndex(index);
        return new DateTime(_values[index].PrimitiveData, DateTimeKind.Utc);
    }

    public object? GetValue(int index, ColumnType type)
    {
        EnsureIndex(index);
        return type switch
        {
            ColumnType.String => _values[index].StringData,
            ColumnType.Byte => (byte)_values[index].PrimitiveData,
            ColumnType.Int32 => (int)_values[index].PrimitiveData,
            ColumnType.Int64 => _values[index].PrimitiveData,
            ColumnType.Float => BitConverter.Int32BitsToSingle((int)_values[index].PrimitiveData),
            ColumnType.Double => BitConverter.Int64BitsToDouble(_values[index].PrimitiveData),
            ColumnType.Boolean => _values[index].PrimitiveData != 0,
            ColumnType.DateTime => new DateTime(_values[index].PrimitiveData, DateTimeKind.Utc),
            _ => null
        };
    }
}
