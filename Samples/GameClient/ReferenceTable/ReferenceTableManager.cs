using System.Reflection;
using SP.Core;
using SP.Shared.Resource.Table;

namespace SP.Shared.Resource;

public sealed partial class ReferenceTableManager : Singleton<ReferenceTableManager>
{
    private readonly Dictionary<string, RefTableSchema> _schemaMap = new();
    private readonly Dictionary<string, RefTableData> _tableMap = new();

    public void Initialize(
        IEnumerable<RefTableSchema> schemas,
        IEnumerable<RefTableData> tables)
    {
        _schemaMap.Clear();
        _tableMap.Clear();
        
        foreach (var s in schemas)
            _schemaMap.Add(s.Name, s);
        
        foreach (var t in tables)
            _tableMap.Add(t.Name, t);

        BuildAll();
    }

    private bool TryGet(string name, out RefTableSchema schema, out RefTableData table)
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

        foreach (var method in methods)
            method.Invoke(this, null);
    }
}
