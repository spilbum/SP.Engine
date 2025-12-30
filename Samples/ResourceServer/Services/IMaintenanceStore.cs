using System.Collections.Immutable;
using System.Net;
using ResourceServer.DatabaseHandler;
using SP.Shared.Database;
using SP.Shared.Resource;

namespace ResourceServer.Services;

public sealed record MaintenanceEnv(
    bool IsEnabled,
    DateTime StartUtc,
    DateTime EndUtc,
    string MessageId,
    string? Comment);

public sealed record MaintenanceBypass(
    int Id,
    MaintenanceBypassKind Kind,
    string Value,
    string? Comment);

public interface IMaintenanceStore
{
    MaintenanceEnv? GetEnv(ServerGroupType serverGroupType);
    ImmutableArray<MaintenanceBypass> GetBypasses(ServerGroupType serverGroupType);
    Task ReloadAsync(CancellationToken ct = default);
}

public sealed class MaintenanceStore(IDbConnector db) : IMaintenanceStore
{
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    
    private ImmutableDictionary<ServerGroupType, MaintenanceEnv> _envs
        = ImmutableDictionary<ServerGroupType, MaintenanceEnv>.Empty;
    
    private ImmutableDictionary<ServerGroupType, ImmutableArray<MaintenanceBypass>> _bypasses
        = ImmutableDictionary<ServerGroupType, ImmutableArray<MaintenanceBypass>>.Empty;
    
    public MaintenanceEnv? GetEnv(ServerGroupType serverGroupType)
        => CollectionExtensions.GetValueOrDefault(_envs, serverGroupType);
    
    public ImmutableArray<MaintenanceBypass> GetBypasses(ServerGroupType serverGroupType)
        => _bypasses.TryGetValue(serverGroupType, out var list) ? list : ImmutableArray<MaintenanceBypass>.Empty;

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await _reloadLock.WaitAsync(ct);
        try
        {
            using var conn = await db.OpenAsync(ct);

            var newEnvs = await LoadEnvsAsync(conn, ct);
            var newBypasses = await LoadBypassesAsync(conn, ct);
            
            _envs = newEnvs;
            _bypasses = newBypasses;
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private static async Task<ImmutableDictionary<ServerGroupType, MaintenanceEnv>> LoadEnvsAsync(
        DbConn conn,
        CancellationToken ct)
    {
        var builder = ImmutableDictionary.CreateBuilder<ServerGroupType, MaintenanceEnv>();
        foreach (var serverGroupType in Enum.GetValues(typeof(ServerGroupType)).Cast<ServerGroupType>())
        {
            var env = await ResourceDb.GetMaintenanceEnvAsync(conn, serverGroupType, ct);
            if (env == null) continue;

            builder[serverGroupType] = new MaintenanceEnv(
                env.IsEnabled,
                EnsureUtc(env.StartUtc),
                EnsureUtc(env.EndUtc),
                env.MessageId,
                env.Comment
            );
        }

        return builder.ToImmutable();
    }
    
    
    private static async Task<ImmutableDictionary<ServerGroupType, ImmutableArray<MaintenanceBypass>>> LoadBypassesAsync(
        DbConn conn, CancellationToken ct)
    {
        var map = new Dictionary<ServerGroupType, List<MaintenanceBypass>>();
        foreach (var serverGroupType in Enum.GetValues(typeof(ServerGroupType)).Cast<ServerGroupType>())
        {
            var list = await ResourceDb.GetMaintenanceBypassAsync(conn, serverGroupType, ct);
            foreach (var e in list)
            {
                if (!Enum.TryParse<MaintenanceBypassKind>(e.Kind, out var kind))
                    continue;

                if (string.IsNullOrWhiteSpace(e.Value))
                    continue;

                if (!map.TryGetValue(serverGroupType, out var bucket))
                {
                    bucket = [];
                    map.Add(serverGroupType, bucket);
                }

                bucket.Add(new MaintenanceBypass(e.Id, kind, e.Value.Trim(), e.Comment));
            }
        }

        var builder = ImmutableDictionary.CreateBuilder<ServerGroupType, ImmutableArray<MaintenanceBypass>>();
        foreach (var (k, v) in map)
            builder[k] = [..v];

        return builder.ToImmutable();
    }

    private static DateTime EnsureUtc(DateTime dt)
    {
        return dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
        };
    }
}

public static class MaintenanceEvaluator
{
    public static bool IsInMaintenanceWindow(MaintenanceEnv env, DateTime nowUtc)
        => env.IsEnabled && env.StartUtc <= nowUtc && env.EndUtc >= nowUtc;

    public static bool IsBypassed(
        IReadOnlyList<MaintenanceBypass> bypasses,
        string? ip,
        string? deviceId)
    {
        foreach (var b in bypasses)
        {
            switch (b.Kind)
            {
                case MaintenanceBypassKind.IpCidr:
                    if (ip != null && IpCidrMatcher.IsMatch(ip, b.Value))
                        return true;
                    break;
                case MaintenanceBypassKind.DeviceId:
                    if (!string.IsNullOrWhiteSpace(deviceId) &&
                        string.Equals(deviceId, b.Value, StringComparison.OrdinalIgnoreCase))
                        return true;
                    break;
            }
        }
        return false;
    }
}

public static class IpCidrMatcher
{
    public static bool IsMatch(string ipText, string cidrOrIp)
    {
        if (!IPAddress.TryParse(ipText, out var ip))
            return false;

        if (!cidrOrIp.Contains('/'))
            return IPAddress.TryParse(cidrOrIp, out var single) && ip.Equals(single);

        var parts = cidrOrIp.Split('/', 2);
        if (parts.Length != 2) return false;
        if (!IPAddress.TryParse(parts[0], out var network)) return false;
        if (!int.TryParse(parts[1], out var prefix)) return false;
        
        var ipBytes = ip.GetAddressBytes();
        var netBytes = network.GetAddressBytes();
        if (ipBytes.Length != 4 || netBytes.Length != 4) return false;
        if (prefix < 0 || prefix > 32) return false;

        var ipInt = ToUInt32(ipBytes);
        var netInt = ToUInt32(netBytes);

        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        return (ipInt & mask) == (netInt & mask);
    }
    
    private static uint ToUInt32(byte[] b)
        => ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
}
