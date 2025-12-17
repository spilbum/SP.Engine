using ResourceServer.DatabaseHandler;
using ResourceServer.Handlers;
using ResourceServer.Services;
using SP.Shared.Database;

namespace ResourceServer;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDatabaseHandler(this IServiceCollection s, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("ResourceDB") 
                               ?? throw new InvalidOperationException("Resource database connectionString missing");
        s.AddSingleton<IDbConnector>(_ => new MySqlDbConnector(connectionString));
        return s;
    }

    public static IServiceCollection AddResourceServices(this IServiceCollection s)
    {
        s.AddSingleton<IBuildPolicyStore, BuildPolicyStore>();
        s.AddSingleton<IResourcePatchStore, ResourcePatchStore>();
        s.AddSingleton<IResourceConfigStore, ResourceConfigStore>();
        s.AddSingleton<IServerStore, InMemoryServerStore>();
        s.AddSingleton<IResourceReloadService, ResourceReloadService>();
        s.AddHostedService<ResourceWarmupHostedService>();
        return s;
    }
    
    public static IServiceCollection AddJsonGateway(this IServiceCollection s)
    {
        s.AddSingleton<IJsonHandler, SyncServerListHandler>();
        s.AddSingleton<IJsonHandler, CheckClientHandler>();
        s.AddSingleton<IJsonHandler, RefreshResourceServerHandler>();
        s.AddSingleton<JsonDispatcher>();
        return s;
    }
}
