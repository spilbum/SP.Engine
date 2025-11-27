using System;
using System.IO;
using SP.Core.Serialization;

namespace SP.Shared.Resource;


public static class RefsSchemaReader
{
    public static RefTableSchema ReadSchFile(ReadOnlySpan<byte> data)
    {
        var r = new NetReader(data);
        
        if (r.ReadByte() != 'R' ||
            r.ReadByte() != 'S' ||
            r.ReadByte() != 'C' ||
            r.ReadByte() != 'H')
            throw new InvalidDataException("Invalid .sch magic");
        
        var tableName = r.ReadString();
        var columnCount = (int)r.ReadVarUInt();
        
        var schema = new RefTableSchema(tableName);

        for (var i = 0; i < columnCount; i++)
        {
            var name = r.ReadString();
            var type = (ColumnType)r.ReadByte();
            var isKey = r.ReadBool();
            schema.Columns.Add(new RefColumn(name, type, isKey));
        }
        
        return schema;
    }
}
