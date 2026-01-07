using System.Collections.Immutable;
using ResourceServer.DatabaseHandler;
using SP.Shared.Resource;

namespace ResourceServer.Services;

public sealed class LocalizationActive
{
    public ServerGroupType ServerGroupType { get; }
    public StoreType StoreType { get; }
    public int FileId { get; }
    public DateTime UpdatedUtc { get; }

    public LocalizationActive(ResourceDb.LocalizationActiveEntity e)
    {
        if (!Enum.TryParse(e.ServerGroupType, out ServerGroupType serverGroupType))
            throw new ArgumentException($"Invalid server group type: {e.ServerGroupType}");

        if (!Enum.TryParse(e.StoreType, out StoreType storeType))
            throw new ArgumentException($"Invalid store type: {e.StoreType}");

        ServerGroupType = serverGroupType;
        StoreType = storeType;
        FileId = e.FileId;
        UpdatedUtc = e.UpdatedUtc;
    }
}

public interface ILocalizationStore
{
    LocalizationActive? GetActive(ServerGroupType serverGroupType, StoreType storeType);
    Task ReloadAsync(CancellationToken ct = default);
}

public sealed class LocalizationStore(IDbConnector db) : ILocalizationStore
{
    private ImmutableDictionary<(ServerGroupType serverGroupType, StoreType storeType), LocalizationActive> _actives
        = ImmutableDictionary<(ServerGroupType serverGroupType, StoreType storeType), LocalizationActive>.Empty;
    
    private readonly SemaphoreSlim _lock = new(1, 1);
    
    public LocalizationActive? GetActive(ServerGroupType serverGroupType, StoreType storeType)
    {
        var snap = Volatile.Read(ref _actives);
        return CollectionExtensions.GetValueOrDefault(snap, (serverGroupType, storeType));
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = await db.OpenAsync(ct).ConfigureAwait(false);
            var list = await ResourceDb.GetLocalizationActivesAsync(conn, ct).ConfigureAwait(false);
            
            var builder = ImmutableDictionary.CreateBuilder<(ServerGroupType, StoreType), LocalizationActive>();
            foreach (var e in list)
            {
                if (!Enum.TryParse(e.ServerGroupType, out ServerGroupType serverGroupType) ||
                    !Enum.TryParse(e.StoreType, out StoreType storeType))
                    continue;
                
                builder[(serverGroupType, storeType)] = new LocalizationActive(e);
            }
            
            _actives = builder.ToImmutable();
        }
        finally
        {
            _lock.Release();
        }
    }
}
