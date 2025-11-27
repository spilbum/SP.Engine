
namespace OperationTool.Storage;

public sealed class DbSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 3306;
    public string Database { get; set; } = "ResourceDB";
    public string User { get; set; } = "root";
    public string Password { get; set; } = "";
}

public sealed class ToolSettings
{
    public DbSettings Database { get; set; } = new();
    public string LastExcelFolder { get; set; } = Path.Combine(FileSystem.AppDataDirectory, "Excels");
    public string OutputFolder { get; set; } = Path.Combine(FileSystem.AppDataDirectory, "Output");
}

public interface ISettingsStorage
{
    ToolSettings? Load();
    Task SaveAsync(ToolSettings settings);
}


