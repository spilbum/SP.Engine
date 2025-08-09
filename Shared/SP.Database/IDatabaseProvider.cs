using System.Data;
using System.Data.Common;

namespace SP.Database;

public interface IDatabaseProvider
{
    DbConnection CreateConnection(string connectionString);
    string FormatParameterName(string name);
}
