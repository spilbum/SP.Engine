namespace SP.Engine.Database
{
    public class DatabaseConfig
    {
        public string DatabaseType { get; }
        public string PrivateConnectionString { get; }
        public string PublicConnectionString { get; }

        public DatabaseConfig(string databaseType, string privateConnectionString, string publicConnectionString)
        {
            DatabaseType = databaseType;
            PrivateConnectionString = privateConnectionString;
            PublicConnectionString = publicConnectionString;
        }
    }
}
