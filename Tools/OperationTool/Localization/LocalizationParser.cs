using ClosedXML.Excel;
using OperationTool.Excel;

namespace OperationTool.Localization;

public sealed record LocalizationParseResult(
    int TotalKeys,
    IReadOnlyList<string> Languages,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ByLanguage);


public static class LocalizationParser
{
    private const string KeyHeader = "key";

    public static LocalizationParseResult ParseFile(string xlsxPath)
    {
        using var wb = new XLWorkbook(xlsxPath);
        var ws = wb.Worksheets.First();
        var grid = XlsxGridReader.ReadSheet(ws);
        return ParseGrid(grid);
    }

    private static LocalizationParseResult ParseGrid(SheetGrid grid)
    {
        if (grid.RowCount < 2)
            throw new InvalidDataException("Localization sheet must have at least header + 1 data row.");

        const int headerRow = 0;
        
        var headers = new List<string>(grid.RowCount);
        for (var c = 0; c < grid.ColumnCount; c++)
            headers.Add(grid.Get(headerRow, c).Trim());

        var keyColIndex = FindHeaderIndex(headers, KeyHeader);
        if (keyColIndex < 0)
            throw new InvalidDataException("Header 'Key' not found (case-insensitive).");

        var languages = new List<string>();
        var lanCols = new List<(string lang, int col)>();
        var lanSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var c = 0; c < grid.ColumnCount; c++)
        {
            if (c == keyColIndex)
                continue;

            var lang = grid.Get(headerRow, c).Trim();
            if (string.IsNullOrWhiteSpace(lang))
                continue;

            //ValidateLanguageCode(lang, grid.Address(headerRow, c));
            
            if (!lanSet.Add(lang))
                throw new InvalidDataException($"Duplicate language column: '{lang}' at {grid.Address(headerRow, c)}.");
            
            languages.Add(lang);
            lanCols.Add((lang, c));
        }
        
        var byLanguage = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var lang in languages)
            byLanguage[lang] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        var keySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalKeys = 0;

        for (var r = 1; r < grid.RowCount; r++)
        {
            if (grid.IsRowEmpty(r))
                continue;

            var key = grid.Get(r, keyColIndex).Trim();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            ValidateKey(key, grid.Address(r, keyColIndex));

            if (!keySet.Add(key))
                throw new InvalidDataException($"Duplicate key: '{key}' at {grid.Address(r, keyColIndex)}");
            
            totalKeys++;
            
            foreach (var (lang, c) in lanCols)
            {
                var text = grid.Get(r, c).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    byLanguage[lang][key] = text;
            }
        }

        var ro = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        foreach (var (lang, map) in byLanguage)
            ro[lang] = map;

        return new LocalizationParseResult(totalKeys, languages, ro);
    }
    private static int FindHeaderIndex(List<string> headers, string name)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static void ValidateKey(string key, string at)
    {
        if (key.Any(char.IsWhiteSpace))
            throw new InvalidDataException($"Key must not contain whitespace: '{key}' at {at}.");
    }
}



