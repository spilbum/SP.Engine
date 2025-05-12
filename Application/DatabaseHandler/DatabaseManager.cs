
namespace DatabaseHandler
{
    public class DatabaseManager(IDatabaseConnectionFactory databaseConnectionFactory)
    {
        private readonly Dictionary<string, DatabaseConfig> _databaseConfigDict = new();

        private readonly IDatabaseConnectionFactory _databaseConnectionFactory = databaseConnectionFactory ?? throw new ArgumentNullException(nameof(databaseConnectionFactory));

        public void AddConfig(DatabaseConfig config)
        {
            if (string.IsNullOrEmpty(config.DatabaseType))
                throw new ArgumentException("DbType cannot be null or empty");

            if (string.IsNullOrEmpty(config.PrivateConnectionString) &&
                string.IsNullOrEmpty(config.PublicConnectionString))
                throw new ArgumentException("Connection string cannot be null or empty");

            if (!_databaseConfigDict.TryAdd(config.DatabaseType, config))
                throw new ArgumentException($"Duplicate database type: {config.DatabaseType}");
        }

        public DatabaseConnection Open(string databaseType, bool isPublic = false)
        {
            try
            {
                if (!_databaseConfigDict.TryGetValue(databaseType, out var config))
                    throw new ArgumentException($"DatabaseConfig not found. databaseType={databaseType}");

                var connectionString = isPublic ? config.PublicConnectionString : config.PrivateConnectionString;
                if (string.IsNullOrEmpty(connectionString))
                    throw new InvalidOperationException("ConnectionString cannot be null or empty");

                var dbConnection = _databaseConnectionFactory.GetConnection(connectionString);
                if (null == dbConnection)
                    throw new InvalidOperationException($"Invalid database. databaseType={databaseType}");
                
                var connection = new DatabaseConnection(dbConnection, config.SqlType);
                connection.Open();
                return connection;
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(
                    $"Failed to open database. databaseType={databaseType}, exception={e.Message}\r\nstackTrace={e.StackTrace}");   
            }
        }
    }
}
