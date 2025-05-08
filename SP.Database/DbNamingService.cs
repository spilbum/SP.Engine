using System.Globalization;
using System.Text.RegularExpressions;

namespace SP.Database
{
    public enum ENamingCaseType
    {
        PascalCase,
        SnakeCase,
    }
    
    public class DatabaseNamingConventionSettings
    {
        public string StoredProcedurePrefix { get; set; } = "usp_";
        public string ParameterPrefix { get; set; } = "p_";
        public ENamingCaseType NamingCaseType { get; set; } = ENamingCaseType.SnakeCase;

        public DatabaseNamingConventionSettings WithStoredProcedurePrefix(string prefix)
        {
            StoredProcedurePrefix = prefix;
            return this;
        }

        public DatabaseNamingConventionSettings WithParameterPrefix(string prefix)
        {
            ParameterPrefix = prefix;
            return this;
        }

        public DatabaseNamingConventionSettings UseSnakeCase()
        {
            NamingCaseType = ENamingCaseType.SnakeCase;
            return this;
        }

        public DatabaseNamingConventionSettings UsePascalCase()
        {
            NamingCaseType = ENamingCaseType.PascalCase;
            return this;
        }
    }

    public class DbNamingService
    {
        private readonly DatabaseNamingConventionSettings _settings;

        public DbNamingService(DatabaseNamingConventionSettings settings)
        {
            _settings = settings;
        }

        public string ConvertColumnName(string columnName)
        {
            return ToDatabaseFormat(columnName, string.Empty, _settings.NamingCaseType);
        }

        public string ConvertParameterName(string paramName)
        {
            return ToDatabaseFormat(paramName, _settings.ParameterPrefix, _settings.NamingCaseType);
        }

        public string ConvertStoredProcedureName(string storedProcedureName)
        {
            return ToDatabaseFormat(storedProcedureName, _settings.StoredProcedurePrefix, _settings.NamingCaseType);
        }
        
        private static string ToDatabaseFormat(string name, string prefix, ENamingCaseType caseType)
        {
            var convertedName = caseType == ENamingCaseType.SnakeCase ? ToSnakeCase(name) : ToPascalCase(name);
            return $"{prefix}{convertedName}";
        }

        private static string ToSnakeCase(string input)
        {
            return string.IsNullOrEmpty(input) ? input : Regex.Replace(input, "([a-z])([A-Z])", "$1_$2").ToLower();
        }

        private static string ToPascalCase(string input)
        {
            return string.IsNullOrEmpty(input)
                ? input
                : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(input.Replace("_", "")).Replace(" ", "");
        }
    }
}
