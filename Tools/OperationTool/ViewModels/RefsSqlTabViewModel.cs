using System.Collections.ObjectModel;
using System.Data;
using System.Text;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using OperationTool.Diff;
using OperationTool.Services;
using SP.Shared.Resource;
using SP.Shared.Resource.SQL;

namespace OperationTool.ViewModels;

public enum SqlKind
{
    Unknown,
    MySql,
    SqlServer
}

public sealed class TableSqlModel(string tableName) : ViewModelBase
{
    private string _createTableQuery = string.Empty;
    private string _insertRowsQuery = string.Empty;

    public string CreateTableQuery
    {
        get => _createTableQuery;
        set => SetProperty(ref _createTableQuery, value);
    }

    public string InsertRowsQuery
    {
        get => _insertRowsQuery;
        set => SetProperty(ref _insertRowsQuery, value);
    }

    public string TableName { get; } = tableName;
}

public sealed class RefsSqlTabViewModel : ViewModelBase
{
    private readonly IFilePicker _filePicker;
    private readonly IDbConnector _dbConnector;
    private string _schsFilePath = string.Empty;
    private string _refsFilePath = string.Empty;
    private bool _isBusy;
    private SqlKind _selectedSqlKind = SqlKind.Unknown;
    private TableSqlModel? _selectedTable;

    public TableSqlModel? SelectedTable
    {
        get => _selectedTable;
        set => SetProperty(ref _selectedTable, value);
    }
    
    public SqlKind SelectedSqlKind
    {
        get => _selectedSqlKind;
        set => SetProperty(ref _selectedSqlKind, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                GenerateSqlCommand.RaiseCanExecuteChanged();
        }
    }
    
    public string RefsFilePath
    {
        get => _refsFilePath;
        private set
        {
            if (SetProperty(ref _refsFilePath, value))
                GenerateSqlCommand.RaiseCanExecuteChanged();
        }
    }

    public string SchsFilePath
    {
        get => _schsFilePath;
        private set
        {
            if (SetProperty(ref _schsFilePath, value))
                GenerateSqlCommand.RaiseCanExecuteChanged();
        }
    }

    public RefsSqlTabViewModel(IFilePicker filePicker, IDbConnector dbConnector)
    {
        _filePicker = filePicker;
        _dbConnector = dbConnector;

        foreach (SqlKind dbKind in Enum.GetValues(typeof(SqlKind)))
        {
            if (dbKind == SqlKind.Unknown)
                continue;
            DbKinds.Add(dbKind);
        }
        SelectedSqlKind = DbKinds.FirstOrDefault();
        
        BrowseSchsCommand = new AsyncRelayCommand(BrowseSchsAsync);
        BrowseRefsCommand = new AsyncRelayCommand(BrowseRefsAsync);
        GenerateSqlCommand = new AsyncRelayCommand(GenerateSqlAsync, CanGenerateSql);
        ExecuteQueryCommand = new AsyncRelayCommand(ExecuteQueryAsync, CanExecuteQuery);
    }

    public ObservableCollection<SqlKind> DbKinds { get; } = [];
    public ObservableCollection<TableSqlModel> Tables { get; } = [];

    public AsyncRelayCommand BrowseSchsCommand { get; }
    public AsyncRelayCommand BrowseRefsCommand { get; }
    public AsyncRelayCommand GenerateSqlCommand { get; }
    public AsyncRelayCommand ExecuteQueryCommand { get; }

    private async Task BrowseSchsAsync()
    {
        var result = await _filePicker.PickAsync();
        if (result is null) return;
        if (!Utils.ValidateExtension(result, "schs"))
        {
            await Toast.Make("Only SCHS files can be selected.").Show(CancellationToken.None);
            return;
        }

        SchsFilePath = result.FullPath;
    }

    private async Task BrowseRefsAsync()
    {
        var result = await _filePicker.PickAsync();
        if (result is null) return;
        if (!Utils.ValidateExtension(result, "refs"))
        {
            await Toast.Make("Only REFS files can be selected.").Show(CancellationToken.None);
            return;
        }

        RefsFilePath = result.FullPath;
    }

    private async Task GenerateSqlAsync()
    {
         IsBusy = true;
        var cts = new CancellationTokenSource();
        var ct = cts.Token;

        try
        {
            Tables.Clear();
            var snap = await RefsSnapshotFactory.FromPackAsync(SchsFilePath, RefsFilePath, ct);

            var dialect = CreateDialect(SelectedSqlKind);
            foreach (var (name, table) in snap.Tables)
            {
                var item = new TableSqlModel(name)
                {
                    CreateTableQuery = TableSqlBuilder.BuildCreateTableSql(table.Schema, dialect)
                };

                var sb = new StringBuilder();
                foreach (var insertSql in TableSqlBuilder.BuildBatchInsertSql(
                             table.Schema,
                             table.Data,
                             dialect,
                             batchSize: 1000))
                {
                    sb.AppendLine(insertSql);
                }
                    
                item.InsertRowsQuery = sb.ToString();
                Tables.Add(item);
            }
            
            SelectedTable = Tables.FirstOrDefault();
            await Toast.Make("SQL generate successfully").Show(ct);
        }
        catch (Exception e)
        {
            await Toast.Make($"An exception occurred: {e.Message}", ToastDuration.Long).Show(ct);
        }
        finally
        {
            IsBusy = false;
        }
    }
    
    private bool CanGenerateSql()
        => !IsBusy && !string.IsNullOrEmpty(SchsFilePath) && !string.IsNullOrEmpty(RefsFilePath);

    private async Task ExecuteQueryAsync()
    {
        IsBusy = true;
        var cts = new CancellationTokenSource();
        var ct = cts.Token;

        try
        {
            using var conn = await _dbConnector.OpenAsync(ct);
            await conn.BeginTransactionAsync(ct);

            foreach (var table in Tables)
            {
                try
                {
                    var cmd = conn.CreateCommand(CommandType.Text, table.CreateTableQuery);
                    await cmd.ExecuteNonQueryAsync(ct);

                    var cmd2 = conn.CreateCommand(CommandType.Text, table.InsertRowsQuery);
                    await cmd2.ExecuteNonQueryAsync(ct);
                }
                catch (Exception e)
                {
                    await conn.RollbackAsync(ct);
                    throw new InvalidOperationException($"Failed to execute sql for '{table.TableName}': {e.Message}", e);
                }
            }
            
            await conn.CommitAsync(ct);
            await Toast.Make("SQL execute successfully").Show(ct);
        }
        catch (Exception e)
        {
            await Toast.Make($"An exception occurred: {e.Message}", ToastDuration.Long).Show(ct);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanExecuteQuery()
        => !IsBusy && SelectedTable != null;
    
    private static ISqlSyntax CreateDialect(SqlKind kind) =>
        kind switch
        {
            SqlKind.MySql => new MySqlSyntax(),
            SqlKind.SqlServer => new SqlServerSyntax(),
            SqlKind.Unknown => throw new InvalidOperationException(),
            _ => throw new NotSupportedException()
        };
}
