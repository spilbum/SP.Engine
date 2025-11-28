using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Storage;
using OperationTool.DatabaseHandler;
using OperationTool.Excel;
using OperationTool.Models;
using OperationTool.Storage;
using SP.Shared.Resource;

namespace OperationTool.ViewModels;

public sealed class GenerateFileViewModel : ViewModelBase
{
    private readonly IExcelService _excelService;
    private readonly IDbConnector _dbConnector;
    private readonly IFolderPicker _folderPicker;
    private readonly ISettingsProvider _settingsProvider;

    private string _outputFolder = string.Empty;
    private string _excelFolder = string.Empty;
    private bool isExcelLoaded;
    private int _totalTableCount;
    private int _checkedTableCount;
    private bool _isAllChecked;
    private bool _isDevelopment;
    private RefsFileModel? _selectedOriginRefsFile;
    private int _fileId;
    private string _comment = string.Empty;
    private bool _isAutoComment = true;
    private bool _isGenerateCode = true;
    private bool _isOriginEnabled;
    private bool _isOutputSelected;

    public string OutputFolder
    {
        get => _outputFolder;
        set
        {
            if (SetProperty(ref _outputFolder, value))
            {
                OnPropertyChanged(nameof(OutputFolderColor));
            }
        }
    }

    public string ExcelFolder
    {
        get => _excelFolder;
        set
        {
            if (SetProperty(ref _excelFolder, value))
            {
                OnPropertyChanged(nameof(ExcelFolderColor));
            }
        }
    }
    
    public bool IsOriginEnabled
    {
        get => _isOriginEnabled;
        set => SetProperty(ref _isOriginEnabled, value);
    }

    public bool IsGenerateCode
    {
        get => _isGenerateCode;
        set => SetProperty(ref _isGenerateCode, value);
    }

    public bool IsAutoComment
    {
        get => _isAutoComment;
        set => SetProperty(ref _isAutoComment, value);
    }

    public int FileId
    {
        get => _fileId;
        private set => SetProperty(ref _fileId, value);
    }

    public string Comment
    {
        get => _comment;
        set => SetProperty(ref _comment, value);
    }

    public RefsFileModel? SelectedOriginRefsFile
    {
        get => _selectedOriginRefsFile;
        set => SetProperty(ref _selectedOriginRefsFile, value);
    }

    public bool IsDevelopment
    {
        get => _isDevelopment;
        set => SetProperty(ref _isDevelopment, value);
    }

    public bool IsAllChecked
    {
        get => _isAllChecked;
        set
        {
            if (!SetProperty(ref _isAllChecked, value)) return;
            foreach (var item in ExcelTables)
                item.IsChecked = value;
            UpdateCheckedCount();
        }
    }

    public int CheckedTableCount
    {
        get => _checkedTableCount;
        private set
        {
            if (SetProperty(ref _checkedTableCount, value))
                GenerateCommand.RaiseCanExecuteChanged();
        }
    }

    public int TotalTableCount
    {
        get => _totalTableCount;
        private set => SetProperty(ref _totalTableCount, value);
    }

    public bool IsExcelLoaded
    {
        get => isExcelLoaded;
        private set
        {
            if (SetProperty(ref isExcelLoaded, value))
            {
                OnPropertyChanged(nameof(ExcelFolderColor));
                GenerateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsOutputSelected
    {
        get => _isOutputSelected;
        private set
        {
            if (SetProperty(ref _isOutputSelected, value))
            {
                OnPropertyChanged(nameof(OutputFolderColor));
                GenerateCommand.RaiseCanExecuteChanged();
            }
        }
    }
    
    public Color OutputFolderColor => 
        string.IsNullOrEmpty(OutputFolder) || !IsOutputSelected ? Colors.Red : Colors.Green;
    
    public Color ExcelFolderColor =>
        string.IsNullOrEmpty(ExcelFolder) || !IsExcelLoaded ? Colors.Red : Colors.Green;

    public ObservableCollection<ExcelTableModel> ExcelTables { get; } = [];
    public ObservableCollection<RefsFileModel> OriginRefsFiles { get; } = [];
    public AsyncRelayCommand BrowseExcelFolderCommand { get; }
    public AsyncRelayCommand BrowseOutputFolderCommand { get; }
    public AsyncRelayCommand GenerateCommand { get; }

    public GenerateFileViewModel(
        IExcelService excelService,
        IDbConnector dbConnector,
        IFolderPicker folderPicker, 
        ISettingsProvider settingsProvider)
    {
        _excelService = excelService;
        _dbConnector = dbConnector;
        _folderPicker = folderPicker;
        _settingsProvider = settingsProvider;

        var s = settingsProvider.Current;
        OutputFolder = s.OutputFolder;
        ExcelFolder = s.LastExcelFolder;
        
        BrowseExcelFolderCommand = new AsyncRelayCommand(BrowseExcelFolderAsync);
        BrowseOutputFolderCommand = new AsyncRelayCommand(BrowseOutputFolderAsync);
        GenerateCommand = new AsyncRelayCommand(GenerateAsync, CanGenerate);
    }

    private async Task GenerateAsync(object? state)
    {
        var confirm = await Shell.Current.DisplayAlert(
            "Confirm",
            "Are you sure you want to generate the refs file?",
            "Generate",
            "Cancel");
        
        if (!confirm)
            return;
        
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = cts.Token;
        
        using var conn = await _dbConnector.OpenAsync(ct);
        await conn.BeginTransactionAsync(ct);

        try
        {
            var entity = new ResourceDb.ResourceRefsFileEntity
            {
                FileId = FileId,
                Comment = Comment,
                IsDevelopment = IsDevelopment
            };

            await ResourceDb.InsertResourceRefsFileAsync(conn, entity, ct);

            var versionDir = Path.Combine(OutputFolder, $"{FileId}");
            var tables = ExcelTables
                .Where(table => table.IsChecked)
                .ToList();

            // .sch
            var schsDir = Path.Combine(versionDir, "schs");
            foreach (var table in tables)
            {
                await RefsSchemaWriter.WriteSchFileAsync(
                    table.GetSchema(), 
                    Path.Combine(schsDir, $"{table.Name}.sch"), 
                    ct);   
            }

            await RefsPackWriter.CreateSchsFileAsync(
                schsDir,
                Path.Combine(versionDir, $"{FileId}.schs"),
                ct);
            
            // .ref
            var refsDir = Path.Combine(versionDir, "refs");
            foreach (var table in tables)
            {
                await RefsDataWriter.WriteRefFileAsync(
                    table.GetSchema(),
                    table.GetData(),
                    Path.Combine(refsDir, $"{table.Name}.ref"),
                    ct);
            }

            await RefsPackWriter.CreateRefsFileAsync(
                refsDir,
                Path.Combine(versionDir, $"{FileId}.refs"),
                ct);

            var codeDir = IsDevelopment
                ? Path.Combine(versionDir, "code", "dev")
                : Path.Combine(versionDir, "code");

            if (IsGenerateCode)
            {
                var schemas = tables.Select(t => t.GetSchema()).ToList();
                ReferenceCodeGenerator.Generate(schemas, codeDir, "SP.Shared.Resource");
            }
            
            // todo:S3 업로드
            
            await conn.CommitAsync(ct);
            await Utils.OpenFolderAsync(versionDir);
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception e)
        {
            await Toast.Make($"An exception occurred: {e.Message}", ToastDuration.Long).Show(ct);
            await conn.RollbackAsync(ct);
        }
    }

    private bool CanGenerate()
        => isExcelLoaded && IsOutputSelected && CheckedTableCount > 0;

    private async Task LoadExcelAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ExcelFolder))
        {
            await Toast.Make("Please specify a valid Excel folder path.", ToastDuration.Long).Show(ct);
            return;
        }
        
        try
        {
            ExcelTables.Clear();

            var tables = await _excelService.LoadFromFolderAsync(ExcelFolder, ct);
            foreach (var vm in tables.Select(table => new ExcelTableModel(table)))
            {
                vm.PropertyChanged += OnTableItemPropertyChanged;
                ExcelTables.Add(vm);
            }

            TotalTableCount = ExcelTables.Count;
            IsExcelLoaded = TotalTableCount > 0;
        }
        catch (Exception ex)
        {
            IsExcelLoaded = false;
            await Toast.Make($"An exception occurred: {ex.Message}", ToastDuration.Long).Show(ct);
        }
    }

    private async Task LoadOriginAsync(CancellationToken ct)
    {
        try
        {
            using var conn = await _dbConnector.OpenAsync(ct);

            FileId = await ResourceDb.GetLatestResourceRefsFileIdAsync(conn, ct) + 1;
            
            var files = await ResourceDb.GetResourceRefsFiles(conn, ct);
            
            OriginRefsFiles.Clear();
            foreach (var entity in files.OrderByDescending(entity => entity.FileId))
            {
                if (entity.IsDevelopment)
                    continue;
                
                var file = new RefsFileModel(entity);
                OriginRefsFiles.Add(file);
            }

            SelectedOriginRefsFile = OriginRefsFiles.FirstOrDefault();
            IsOriginEnabled = OriginRefsFiles.Count > 0;
        }
        catch (Exception e)
        {
            await Toast.Make($"An exception occurred: {e.Message}", ToastDuration.Long).Show(ct);
        }
    }
    
    private async Task BrowseExcelFolderAsync(object? parameter)
    {
        
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        try
        {
            var result = await _folderPicker.PickAsync(ct);
            if (!result.IsSuccessful || result.Folder is null)
                return;

            ExcelFolder = result.Folder.Path;

            var s = _settingsProvider.Current;
            s.LastExcelFolder = ExcelFolder;
            await _settingsProvider.SaveAsync();
            
            await LoadExcelAsync(ct);
            await LoadOriginAsync(ct);
            await Toast.Make(IsExcelLoaded ? $"Excel '{TotalTableCount}' files loaded." : "Excel files not loaded.", ToastDuration.Long).Show(ct);
        }
        catch (Exception e)
        {
            await Toast.Make($"An exception occurred: {e.Message}", ToastDuration.Long).Show(ct);
        }
    }

    private async Task BrowseOutputFolderAsync(object? parameter)
    {
        var result = await _folderPicker.PickAsync();
        if (!result.IsSuccessful || result.Folder is null)
            return;
        
        OutputFolder = result.Folder.Path;
        IsOutputSelected = true;
        
        var s = _settingsProvider.Current;
        s.OutputFolder = OutputFolder;
        await _settingsProvider.SaveAsync();
    }
    
    private void OnTableItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExcelTableModel.IsChecked))
            UpdateCheckedCount();
    }
    
    private void UpdateCheckedCount()
    {
        CheckedTableCount = ExcelTables.Count(x => x.IsChecked);
        
        var allChecked = TotalTableCount > 0 && CheckedTableCount == TotalTableCount;
        if (_isAllChecked != allChecked)
        {
            _isAllChecked = allChecked;
            OnPropertyChanged(nameof(IsAllChecked));
        }

        GenerateCommand.RaiseCanExecuteChanged();
    }
}
