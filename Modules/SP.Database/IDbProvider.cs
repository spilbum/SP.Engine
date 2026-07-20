using System.Data.Common;

namespace SP.Database;

public interface IDbProvider
{
    DbConnection CreateConnection(string connectionString);
    string FormatParameterName(string name);
}
