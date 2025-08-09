using System.Data;
using System.Data.Common;

namespace SP.Database;

public class DbConfig
{
    public string? DbType { get; set; }
    public string? PrivateConnectionString { get; set; }
    public string? PublicConnectionString { get; set; }
}

public class DatabaseConfigBuilder
{
    private readonly DbConfig _config = new();
    
    public static DatabaseConfigBuilder Create() => new();

    public DatabaseConfigBuilder WithDatabaseType(string databaseType)
    {
        _config.DbType = databaseType;
        return this;
    }

    public DatabaseConfigBuilder WithPrivateConnectionString(string privateConnectionString)
    {
        _config.PrivateConnectionString = privateConnectionString;
        return this;
    }

    public DatabaseConfigBuilder WithPublicConnectionString(string publicConnectionString)
    {
        _config.PublicConnectionString = publicConnectionString;
        return this;
    }
    
    public DbConfig Build() => _config;
}


