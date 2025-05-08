using System;
using System.Collections.Generic;

namespace SP.Database
{
    public class DatabaseManager
    {
        private readonly Dictionary<string, DatabaseConfig> _databaseConfigDict =
            new Dictionary<string, DatabaseConfig>();

        private readonly DbNamingService _namingService;
        private readonly IDatabaseConnectionFactory _databaseConnectionFactory;

        public DatabaseManager(IDatabaseConnectionFactory databaseConnectionFactory, DatabaseNamingConventionSettings settings)
        {
            _databaseConnectionFactory = databaseConnectionFactory ?? throw new ArgumentNullException(nameof(databaseConnectionFactory));
            _namingService = new DbNamingService(settings);
        }

        public void AddConfig(DatabaseConfig config)
        {
            if (string.IsNullOrEmpty(config.DatabaseType))
                throw new ArgumentException("DbType cannot be null or empty");

            if (string.IsNullOrEmpty(config.PrivateConnectionString) &&
                string.IsNullOrEmpty(config.PublicConnectionString))
                throw new ArgumentException("Connection string cannot be null or empty");

            _databaseConfigDict.Add(config.DatabaseType, config);
        }

        public SqlConnection Open(string databaseType, bool isPublic = false)
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
                
                var connection = new SqlConnection(dbConnection, _namingService);
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
