using System.Data.Common;

namespace SP.Database;

public interface IDatabaseConnectionFactory
{
    DbConnection GetConnection(string key);
}
