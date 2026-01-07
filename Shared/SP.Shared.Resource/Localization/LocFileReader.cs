using System;
using System.IO;
using SP.Core.Serialization;

namespace SP.Shared.Resource.Localization;

public static class LocFileReader
{
    public static LocFile Read(ReadOnlySpan<byte> data)
    {
        var r = new NetReader(data);

        if (r.ReadByte() != (byte)'L' ||
            r.ReadByte() != (byte)'L' ||
            r.ReadByte() != (byte)'O' ||
            r.ReadByte() != (byte)'C')
            throw new InvalidDataException("Invalid LOC magic");

        var language = r.ReadString();
        var count = (int)r.ReadVarUInt();

        var locFile = new LocFile(language);
        for (var i = 0; i < count; i++)
        {
            var key = r.ReadString();
            var value = r.ReadString();
            if (!locFile.Map.TryAdd(key, value))
                throw new InvalidDataException($"Duplicate key in loc file: {key}");
        }
        
        return locFile;
    }
}
