namespace ResourceServer.Services;

public class ResourceWarmupHostedService(
    IResourceReloadService reloadService,
    ILogger<ResourceWarmupHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        logger.LogInformation("Resource warmup started.");

        try
        {
            await reloadService.ReloadAllAsync(ct).ConfigureAwait(false);
            logger.LogInformation("Resource warmup completed.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Resource warmup failed.");
        }
    }
    
    public Task StopAsync(CancellationToken ct) 
        => Task.CompletedTask;
}
