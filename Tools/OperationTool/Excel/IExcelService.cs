namespace OperationTool.Excel;

public interface IExcelService
{
    Task<List<ExcelTable>> LoadFromFolderAsync(string folderPath, CancellationToken ct);
}

public sealed class ExcelService : IExcelService
{
    public async Task<List<ExcelTable>> LoadFromFolderAsync(string folderPath, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var tables = new List<ExcelTable>();
            
            var files = Directory.GetFiles(folderPath, "*.xlsx", SearchOption.AllDirectories);
            foreach (var path in files)
                tables.AddRange(ExcelTableParser.LoadWorkbook(path));
            
            return tables;
        }, ct).ConfigureAwait(false);
    }
}
