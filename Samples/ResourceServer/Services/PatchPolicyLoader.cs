namespace ResourceServer.Services;

public sealed class PatchPolicyLoader(PatchPolicyStore store) : IPatchPolicyLoader
{
    public async Task<bool> ReloadAsync(CancellationToken ct)
        => await store.ReloadAsync(ct);
}
