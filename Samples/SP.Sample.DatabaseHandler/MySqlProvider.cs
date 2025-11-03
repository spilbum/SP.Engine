using System.Data.Common;
using MySql.Data.MySqlClient;
using SP.Shared.Database;

namespace SP.Sample.DatabaseHandler;

public class MySqlProvider : IDbProvider
{
    public DbConnection CreateConnection(string connectionString)
    {
        return new MySqlConnection(connectionString);
    }

    public string FormatParameterName(string name)
    {
        return $"p_{name}";
    }
}
