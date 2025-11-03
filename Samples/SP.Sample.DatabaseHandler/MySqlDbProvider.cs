using System.Data.Common;
using MySql.Data.MySqlClient;
using SP.Shared.Database;

namespace SP.Sample.DatabaseHandler;

public class MySqlDbProvider : IDbProvider
{
    public DbConnection CreateConnection(string connectionString)
        => new MySqlConnection(connectionString);

    public string FormatParameterName(string name)
        => $"p_{name}";
}
