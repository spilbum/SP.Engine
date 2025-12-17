using System.Data.Common;
using MySqlConnector;
using SP.Shared.Database;

namespace OperationTool.Services;

public class MySqlDbProvider : IDbProvider
{
    public DbConnection CreateConnection(string connectionString)
        => new MySqlConnection(connectionString);

    public string FormatParameterName(string name)
        => $"p_{name}";
}
