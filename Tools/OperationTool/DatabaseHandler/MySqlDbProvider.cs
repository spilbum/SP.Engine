using System.Data.Common;
using MySqlConnector;
using SP.Shared.Database;

namespace OperationTool.DatabaseHandler;

public class MySqlDbProvider : IDbProvider
{
    public DbConnection CreateConnection(string connectionString)
        => new MySqlConnection(connectionString);

    public string FormatParameterName(string name)
        => $"p_{name}";
}
