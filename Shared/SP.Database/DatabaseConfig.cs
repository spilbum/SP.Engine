using System.Data;
using System.Data.Common;

namespace SP.Database;

public abstract class DatabaseConfig(string databaseType, string privateConnectionString, string publicConnectionString)
{
    public string DatabaseType { get; } = databaseType;
    public string PrivateConnectionString { get; } = privateConnectionString;
    public string PublicConnectionString { get; } = publicConnectionString;
}

public enum ECommandType
{
    Text = CommandType.Text,
    StoredProcedure = CommandType.StoredProcedure,
}
