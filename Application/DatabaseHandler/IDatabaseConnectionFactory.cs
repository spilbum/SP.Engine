using System.Data.Common;

namespace DatabaseHandler
{
    public enum ESqlType
    {
        SqlServer,
        MySql,
    }
    
    public interface IDatabaseConnectionFactory
    {
        DbConnection GetConnection(string connectionString);
    }
}

