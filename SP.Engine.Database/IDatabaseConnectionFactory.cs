using System.Data.Common;

namespace SP.Engine.Database
{

    public interface IDatabaseConnectionFactory
    {
        DbConnection GetConnection(string connectionString);
    }


}

