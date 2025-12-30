namespace ResourceServer.Services;

public class StartupLoader(
    IBuildPolicyStore buildStore,
    IResourceConfigStore configStore,
    IResourcePatchStore patchStore,
    IMaintenanceStore maintenanceStore,
    ILogger<StartupLoader> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await buildStore.ReloadAsync(ct);
            await configStore.ReloadAsync(ct);
            await patchStore.ReloadAsync(ct);
            await maintenanceStore.ReloadAsync(ct);
            logger.LogInformation("Startup completed.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Startup failed.");
        }
    }
    
    public Task StopAsync(CancellationToken ct) 
        => Task.CompletedTask;
}
