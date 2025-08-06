using System.Data;
using System.Data.Common;

namespace SP.Database;

public abstract class DatabaseConfig(string databaseType, string privateConnectionString, string publicConnectionString)
{
    public string DatabaseType { get; } = databaseType;
    public string PrivateConnectionString { get; } = privateConnectionString;
    public string PublicConnectionString { get; } = publicConnectionString;
}

public enum EDatabaseEngine
{
    SqlServer,
    MySql,
    Unknown,
}

public enum ECommandType
{
    Text = CommandType.Text,
    StoredProcedure = CommandType.StoredProcedure,
}

public static class DbUtil
{
    public static EDatabaseEngine DetectEngine(DbCommand command)
    {
        var ns = command.GetType().Namespace;
        return ns switch
        {
            "System.Data.SqlClient" or "Microsoft.Data.SqlClient" => EDatabaseEngine.SqlServer,
            "MySql.Data.MySqlClient" => EDatabaseEngine.MySql,
            _ => EDatabaseEngine.Unknown,
        };
    }
}

