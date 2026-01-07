using System.Collections.Immutable;
using OperationTool.DatabaseHandler;
using SP.Shared.Resource;

namespace OperationTool.Services;

public sealed class TableTargetResolver
{
    private ImmutableDictionary<string, PatchTarget> _overrides 
        = ImmutableDictionary<string, PatchTarget>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);

    public async Task ReloadAsync(IDbConnector db, CancellationToken ct)
    {
        using var conn = await db.OpenAsync(ct);
        var list = await ResourceDb.GetRefsTableTargetsAsync(conn, ct);
        
        var b = ImmutableDictionary.CreateBuilder<string, PatchTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in list)
            b[e.TableName] = (PatchTarget)e.TargetFlags;
        
        _overrides = b.ToImmutable();
    }

    public PatchTarget Resolve(string tableName, PatchTarget defaultTarget = PatchTarget.Both)
        => CollectionExtensions.GetValueOrDefault(_overrides, tableName, defaultTarget);
}
