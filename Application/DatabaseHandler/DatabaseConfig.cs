namespace DatabaseHandler
{
    public abstract class DatabaseConfig(string databaseType, string privateConnectionString, string publicConnectionString, ESqlType sqlType)
    {
        public string DatabaseType { get; } = databaseType;
        public string PrivateConnectionString { get; } = privateConnectionString;
        public string PublicConnectionString { get; } = publicConnectionString;
        public ESqlType SqlType { get; } = sqlType;
    }
}
