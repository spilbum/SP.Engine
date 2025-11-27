using ResourceServer.DatabaseHandler;
using ResourceServer.Handlers;
using ResourceServer.Services;

namespace ResourceServer;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDatabaseHandler(this IServiceCollection s, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("ResourceDB") 
                               ?? throw new InvalidOperationException("ResourceDb conn string missing");
        s.AddSingleton(new MySqlDbConnector(connectionString));
        return s;
    }

    public static IServiceCollection AddServerDirectory(this IServiceCollection s)
    {
        s.AddSingleton<IServerDirectory, InMemoryServerDirectory>();
        return s;
    }

    public static IServiceCollection AddPatchPolicy(this IServiceCollection s)
    {
        s.AddSingleton<PatchPolicyStore>();
        s.AddSingleton<IPatchPolicyLoader, PatchPolicyLoader>();
        s.AddHostedService<PatchPolicyReloader>();
        return s;
    }
    
    public static IServiceCollection AddJsonGateway(this IServiceCollection s)
    {
        s.AddSingleton<IJsonHandler, SyncServerListHandler>();
        s.AddSingleton<IJsonHandler, CheckClientHandler>();
        s.AddSingleton<IJsonHandler, NotifyPatchHandler>();
        s.AddSingleton<JsonDispatcher>();
        return s;
    }
}
