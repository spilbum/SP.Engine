using System.Text.Json;
using System.Text.Json.Nodes;

namespace GameServer;

public class ServerConfig
{
    public string Name { get; set; } = "";
    public int Port { get; set; } = 0;
}

public class DatabaseConfig
{
    public string Kind { get; set; } = "";
    public string ConnectionString { get; set; } = "";
}

public class ConnectorConfig
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 0;
}

public class AppConfig
{
    public ServerConfig Server { get; set; } = new();
    public DatabaseConfig[] Database { get; set; } = [];
    public ConnectorConfig[] Connector { get; set; } = [];
}

public static class JsonConfigLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    
    public static T Load<T>(string fileName, params string[] addFiles)
    {
        var baseDir = AppContext.BaseDirectory;
        var basePath = Path.Combine(baseDir, fileName);
        
        if (!File.Exists(basePath))
            throw new FileNotFoundException($"File {fileName} not found in {baseDir}");

        var baseNode = ReadAsNode(basePath);
        
        foreach (var addFile in addFiles)
        {
            var full = Path.Combine(baseDir, addFile);
            if (File.Exists(full))
                DeepMerge(baseNode, ReadAsNode(full));
        }

        var json = baseNode.ToJsonString(SerializerOptions);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions)
            ?? throw new InvalidDataException("Deserialization returned null.");
    }

    private static JsonNode ReadAsNode(string path)
    {
        using var fs = File.OpenRead(path);
        var node = JsonNode.Parse(fs, new JsonNodeOptions { PropertyNameCaseInsensitive = true }, DocumentOptions);
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
                    MergeArrayByKey(baseArr, addArr, key);
                    break;
                default:
                    baseObj[key] = addVal.DeepClone();
                    break;
            }
        }
    }

    private static void MergeArrayByKey(JsonArray baseArr, JsonArray addArr, string keyName)
    {
        var index = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in baseArr.OfType<JsonObject>())
        {
            if (e.TryGetPropertyValue(keyName, out var k) && k is JsonValue v && v.TryGetValue<string>(out var s))
                index[s] = e;
        }

        foreach (var item in addArr.OfType<JsonObject>())
        {
            if (item.TryGetPropertyValue(keyName, out var k2) && k2 is JsonValue v2 && v2.TryGetValue<string>(out var s2) && index.TryGetValue(s2, out var target))
                DeepMerge(target, item);
            else
            {
                baseArr.Add(item.DeepClone());
            }
        }
    }
}

