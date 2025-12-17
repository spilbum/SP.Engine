using Microsoft.Extensions.Hosting;

namespace OperationTool.Services;

public sealed class ToolWarmupService(
    IResourceConfigStore configStore,
    ISettingsProvider settingsProvider,
    IDbConnector dbConnector) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var settings = await settingsProvider.LoadAsync(ct);
        var connStr = settings.Database.GetConnectionString();
        dbConnector.AddOrUpdate(connStr);
        await configStore.LoadAsync(ct);
    }
    
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
