using System.Text.Json;
using System.Text.Json.Nodes;

namespace SP.Sample.RankServer;

public class ServerConfig
{
    public string? Name { get; set; }
    public int Port { get; set; }
}

public class DatabaseConfig
{
    public string? Kind { get; set; }
    public string? ConnectionString { get; set; }
}

public class ConnectorConfig
{
    public string? Name { get; set; }
    public string? Host { get; set; }
    public int Port { get; set; }
}

public class AppConfig
{
    public ServerConfig? Server { get; set; }
    public DatabaseConfig[]? Database { get; set; }
    public ConnectorConfig[]? Connector { get; set; }
}

public static class JsonConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };
    
    public static T? Load<T>(string path, params string[] addPaths)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"File {path} not found");

        var baseNode = ReadAsNode(path);
        foreach (var addPath in addPaths)
        {
            var addNode = ReadAsNode(addPath);
            DeepMerge(baseNode, addNode);
        }
        
        var json = baseNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        return JsonSerializer.Deserialize<T>(json, Options);
    }

    private static JsonNode ReadAsNode(string path)
    {
        using var fs = File.OpenRead(path);
        var node = JsonNode.Parse(fs, new JsonNodeOptions { PropertyNameCaseInsensitive = true });
        if (node is null) throw new InvalidDataException($"Invalid JSON file: {path}");
        return node;
    }

    private static void DeepMerge(JsonNode baseNode, JsonNode addNode)
    {
        if (addNode is not JsonObject addObj ||
            baseNode is not JsonObject baseObj)
        {
            return;
        }
        
        foreach (var (key, addVal) in addObj)
        {
            if (!baseObj.TryGetPropertyValue(key, out var baseVal) || baseVal is null || addVal is null)
            {
                baseObj[key] = addVal?.DeepClone();
                continue;
            }

            switch (baseVal)
            {
                case JsonObject when addVal is JsonObject:
                    DeepMerge(baseVal, addVal);
                    break;
                case JsonArray baseArr when addVal is JsonArray addArr:
                {
                    // 설정 추가
                    foreach (var item in addArr)
                        baseArr.Add(item?.DeepClone());
                    break;
                }
                default:
                    baseObj[key] = addVal.DeepClone();
                    break;
            }
        }
    }
}

internal static class Program
{
    private static void Main(string[] args)
    {
        try
        {
            var path = args.Length > 0 ? args[0] : "config.json";
            var config = JsonConfigLoader.Load<AppConfig>(path);
            if (config == null)
                throw new InvalidOperationException("Failed to deserialize config file.");

            using var server = new RankServer();
            if (!server.Initialize(config))
                throw new InvalidOperationException("Failed to initialize");
            if (!server.Start())
                throw new InvalidOperationException("Failed to start");

            while (true) Thread.Sleep(50);
        }
        catch (Exception e)
        {
            Console.WriteLine("An exception occurred: {0}\r\nstackTrace={1}", e.Message, e.StackTrace);
        }
    }
}
