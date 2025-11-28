using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SP.Core;

namespace SP.Shared.Resource;

public sealed partial class ReferenceTableManager : Singleton<ReferenceTableManager>
{
    private readonly Dictionary<string, RefTableSchema> _schemaMap =
        new(StringComparer.Ordinal);
    
    private readonly Dictionary<string, RefTableData> _tableMap =
        new(StringComparer.Ordinal);
    
    public IReadOnlyDictionary<string, RefTableSchema> Schemas => _schemaMap;
    public IReadOnlyDictionary<string, RefTableData> Tables => _tableMap;
    
    public bool IsInitialized { get; private set; }

    public void Clear()
    {
        _schemaMap.Clear();
        _tableMap.Clear();
        IsInitialized = false;
    }

    public void Initialize(
        IEnumerable<RefTableSchema> schemas,
        IEnumerable<RefTableData> tables)
    {
        Clear();
        
        foreach (var schema in schemas)
            _schemaMap.Add(schema.Name, schema);
        
        foreach (var table in tables)
            _tableMap.Add(table.Name, table);

        BuildAll();
        IsInitialized = true;
    }

    internal bool TryGet(string name, out RefTableSchema schema, out RefTableData table)
    {
        if (_schemaMap.TryGetValue(name, out schema!) &&
            _tableMap.TryGetValue(name, out table!))
            return true;
        
        schema = null!;
        table = null!;
        return false;
    }

    private void BuildAll()
    {
        var methods = typeof(ReferenceTableManager)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(m => 
                m.Name.StartsWith("BuildRef", StringComparison.Ordinal) &&
                m.GetParameters().Length == 0);

        foreach (var m in methods)
            m.Invoke(this, null);
    }
}
