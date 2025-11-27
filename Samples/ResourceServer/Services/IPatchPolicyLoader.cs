namespace ResourceServer.Services;

public interface IPatchPolicyLoader
{
    Task<bool> ReloadAsync(CancellationToken ct);
}
