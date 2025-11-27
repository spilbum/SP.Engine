namespace ResourceServer.Services;

public class PatchPolicyReloader(ILogger<PatchPolicyReloader> logger, IPatchPolicyLoader loader) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var changed = await loader.ReloadAsync(stoppingToken);
            logger.LogInformation("PatchPolicy initial load: changed={Changed}", changed);
        }
        catch (Exception e)
        {
            logger.LogError(e, "PatchPolicy initial load failed");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var changed = await loader.ReloadAsync(stoppingToken);
                if (changed)
                    logger.LogInformation("PatchPolicy reloaded (changed)");
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                logger.LogError(e, "PatchPolicy reload failed");
            }
            
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}
