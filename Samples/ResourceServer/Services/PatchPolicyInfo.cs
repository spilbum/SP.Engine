using ResourceServer.DatabaseHandler;
using SP.Shared.Resource;

namespace ResourceServer.Services;

public sealed class PatchPolicyInfo(ResourceDb.PatchPolicyEntity entity)
{
    public PlatformKind Platform { get; init; } = (PlatformKind)entity.Platform;
    public string MinVersion { get; init; } = entity.MinBuildVersion ?? string.Empty;
    public string LatestVersion { get; init; } = entity.LatestBuildVersion ?? string.Empty;
    public int LatestResourceVersion { get; init; } = entity.LatestResourceVersion;
    public string PatchBaseUrl { get; init; } = entity.PatchBaseUrl ?? string.Empty;
    public string StoreUrl { get; init; } = entity.StoreUrl ?? string.Empty;

    public override bool Equals(object? obj)
        => obj is PatchPolicyInfo r &&
           Platform == r.Platform &&
           MinVersion == r.MinVersion &&
           LatestVersion == r.LatestVersion &&
           LatestResourceVersion == r.LatestResourceVersion &&
           PatchBaseUrl == r.PatchBaseUrl &&
           StoreUrl == r.StoreUrl;

    public override int GetHashCode()
        => HashCode.Combine(
            Platform, MinVersion, LatestVersion, LatestResourceVersion, PatchBaseUrl, StoreUrl);
}
