using System.Text.Json;

namespace SP.Sample.GameServer;

public class ServerOptions
{
    public string Name { get; set; } = string.Empty;
    public int Port { get; set; }
}

public class DatabaseOptions
{
    public string Kind { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
}

public class ConnectorOptions
{
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 0;
}

public class AppOptions
{
    public ServerOptions Server { get; set; } = new();
    public DatabaseOptions[] Database { get; set; } = [];
    public ConnectorOptions[] Connector { get; set; } = [];
}

internal static class Program
{
    private static void Main(string[] args)
    {
        try
        {
            var json = File.ReadAllText("appsettings.json");
            var options = JsonSerializer.Deserialize<AppOptions>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (options == null)
                throw new InvalidOperationException("Failed to deserialize appsettings.json");

            using var server = new GameServer();
            if (!server.Initialize(options))
                throw new InvalidOperationException("Failed to initialize server");
            if (!server.Start())
                throw new InvalidOperationException("Failed to start server");

            while (true) Thread.Sleep(50);
        }
        catch (Exception e)
        {
            Console.WriteLine("An exception occurred: {0}\r\nstackTrace={1}", e.Message, e.StackTrace);
        }
    }
}
