using Microsoft.AspNetCore.Mvc.TagHelpers.Cache;

namespace ResourceServer.Services;

public interface IResourceReloadService
{
    Task ReloadAllAsync(CancellationToken ct = default);
}

public class ResourceReloadService(
    IBuildPolicyStore buildStore,
    IResourceConfigStore configStore,
    IResourcePatchStore patchStore) : IResourceReloadService
{
    public async Task ReloadAllAsync(CancellationToken ct = default)
    {
        await Task.WhenAll(
            buildStore.ReloadAsync(ct),
            configStore.ReloadAsync(ct),
            patchStore.ReloadAsync(ct)
        ).ConfigureAwait(false);
    }
}
