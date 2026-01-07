using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Storage;
using OperationTool.DatabaseHandler;
using OperationTool.Excel;
using OperationTool.Models;
using OperationTool.Services;
using SP.Shared.Database;
using SP.Shared.Resource;
using SP.Shared.Resource.CodeGen;
using SP.Shared.Resource.Refs;
using SP.Shared.Resource.Schs;

namespace OperationTool.ViewModels;

public sealed class RefsFile(ResourceDb.RefsFileEntity entity)
{
    public int FileId { get; } = entity.FileId;
    public string? Comment { get; } = entity.Comment;
    public bool IsDevelopment { get; } = entity.IsDevelopment;
}

public sealed class GenerateRefsFileViewModel : ViewModelBase
{
    private readonly IExcelService _excel;
    private readonly IDbConnector _db;
    private readonly IFolderPicker _folderPicker;
    private readonly IFileUploader _uploader;

    private string _outputFolder = string.Empty;
    private string _excelFolder = string.Empty;

    private bool _isBusy;
    private bool _isExcelLoaded;
    private bool _isAllChecked;
    private bool _isDevelopment;
    private bool _isAutoComment;
    private bool _isGenerateCode;
    private bool _isOriginEnabled;
    private bool _isOutputSelected;
    
    private int _totalTableCount;
    private int _checkedTableCount;
    
    private RefsFile? _selectedOriginRefsFile;
    private string? _selectedOriginRefsComment;
    private int _fileId;
    private string _comment = string.Empty;

    public string? SelectedOriginRefsComment
    {
        get => _selectedOriginRefsComment;
        private set => SetProperty(ref _selectedOriginRefsComment, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                RefreshUI();
        }
    }

    public bool CanBrowseExcel => !IsBusy;
    public bool CanBrowseOutput => !IsBusy;
    public bool CanGenerate => 
        IsExcelLoaded && IsOutputSelected && CheckedTableCount > 0 && !IsBusy;

    public string OutputFolder
    {
        get => _outputFolder;
        private set
        {
            if (SetProperty(ref _outputFolder, value))
                RefreshUI();
        }
    }

    public string ExcelFolder
    {
        get => _excelFolder;
        private set
        {
            if (SetProperty(ref _excelFolder, value))
                RefreshUI();
        }
    }
    
    public bool IsOriginEnabled
    {
        get => _isOriginEnabled;
        private set => SetProperty(ref _isOriginEnabled, value);
    }

    public bool IsGenerateCode
    {
        get => _isGenerateCode;
        set => SetProperty(ref _isGenerateCode, value);
    }

    public bool IsAutoComment
    {
        get => _isAutoComment;
        set
        {
            if (!SetProperty(ref _isAutoComment, value)) return;
            if (value) Comment = BuildAutoComment();
        }
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

    public RefsFile? SelectedOriginRefsFile
    {
        get => _selectedOriginRefsFile;
        set
        {
            if (!SetProperty(ref _selectedOriginRefsFile, value)) return;
            SelectedOriginRefsComment = value?.Comment;
        }
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
               RefreshUI();
        }
    }

    public int TotalTableCount
    {
        get => _totalTableCount;
        private set => SetProperty(ref _totalTableCount, value);
    }

    public bool IsExcelLoaded
    {
        get => _isExcelLoaded;
        private set
        {
            if (SetProperty(ref _isExcelLoaded, value))
                RefreshUI();
        }
    }

    public bool IsOutputSelected
    {
        get => _isOutputSelected;
        private set
        {
            if (SetProperty(ref _isOutputSelected, value))
                RefreshUI();
        }
    }
    
    public bool HasDirtyTargets => ExcelTables.Any(x => x.IsTargetDirty);

    public ObservableCollection<ExcelTableModel> ExcelTables { get; } = [];
    public ObservableCollection<RefsFile> OriginRefsFiles { get; } = [];
    public AsyncRelayCommand BrowseExcelCommand { get; }
    public AsyncRelayCommand BrowseOutputCommand { get; }
    public AsyncRelayCommand GenerateCommand { get; }

    public GenerateRefsFileViewModel(
        IExcelService excel,
        IDbConnector db,
        IFolderPicker folderPicker,
        IFileUploader uploader)
    {
        _excel = excel;
        _db = db;
        _folderPicker = folderPicker;
        _uploader = uploader;

        BrowseExcelCommand = new AsyncRelayCommand(BrowseExcelFolderAsync, () => CanBrowseExcel);
        BrowseOutputCommand = new AsyncRelayCommand(BrowseOutputFolderAsync, () => CanBrowseOutput);
        GenerateCommand = new AsyncRelayCommand(GenerateAsync, () => CanGenerate);
    }

    private async Task EnsureTargetsSaveAsync(DbConn conn, CancellationToken ct)
    {
        var dirty = ExcelTables.Where(x => x.IsTargetDirty).ToList();
        if (dirty.Count == 0) return;

        foreach (var t in dirty)
        {
            await ResourceDb.UpsertRefsTableTargetAsync(
                conn,
                new ResourceDb.RefsTableTargetEntity
                {
                    TableName = t.Name,
                    TargetFlags = (byte)t.Target,
                    Comment = "auto saved by generate"
                }, ct);
            
            t.MarkTargetSaved();
        }
        
        OnPropertyChanged(nameof(HasDirtyTargets));
    }

    private async Task GenerateAsync()
    {
        if (!CanGenerate) return;
        
        var ok = await Shell.Current.DisplayAlert(
            "Confirm",
            "Would you like to generate a file?",
            "Ok",
            "Cancel");
        if (!ok)
            return;
        
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = cts.Token;

        try
        {
            IsBusy = true;

            using var conn = await _db.OpenAsync(ct);
            await conn.BeginTransactionAsync(ct);

            await EnsureTargetsSaveAsync(conn, ct);

            await ResourceDb.InsertRefsFileAsync(
                conn, 
                new ResourceDb.RefsFileEntity
                {
                    FileId = FileId,
                    Comment = Comment,
                    IsDevelopment = IsDevelopment
                },
                ct);

            var versionDir = Path.Combine(OutputFolder, $"{FileId}");
            var selected = ExcelTables
                .Where(table => table.IsChecked)
                .ToList();

            var clientTables = selected.Where(t => (t.Target & PatchTarget.Client) != 0).ToList();
            var serverTables = selected.Where(t => (t.Target & PatchTarget.Server) != 0).ToList();

            await BuildAndUploadAsync(FileId, PatchTarget.Client, clientTables, versionDir, ct);
            await BuildAndUploadAsync(FileId, PatchTarget.Server, serverTables, versionDir, ct);

            if (IsGenerateCode)
            {
                var codeDir = IsDevelopment
                    ? Path.Combine(versionDir, "code", "dev")
                    : Path.Combine(versionDir, "code");

                var schemas = selected.Select(t => t.GetSchema()).ToList();
                ReferenceCodeGenerator.Generate(schemas, codeDir, "SP.Shared.Resource");
            }

            await conn.CommitAsync(ct);

            await Utils.OpenFolderAsync(versionDir);
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception e)
        {
            await Utils.AlertAsync(AlertLevel.Error, $"Generate failed: {e.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BuildAndUploadAsync(
        int fileId,
        PatchTarget target,
        List<ExcelTableModel> excelTables,
        string versionDir,
        CancellationToken ct)
    {
        if (excelTables.Count == 0)
            return;

        var suffix = target.ToString().ToLower();
        
        // .sch
        var schsDir = Path.Combine(versionDir, $"schs.{suffix}");
        foreach (var table in excelTables)
        {
            await SchFileWriter.WriteAsync(
                table.GetSchema(),
                Path.Combine(schsDir, $"{table.Name}.sch"),
                ct);
        }

        var schsFilePath = Path.Combine(versionDir, $"{fileId}.{suffix}.schs");
        await SchsPackWriter.WriteAsync(schsDir, schsFilePath, ct);

        // .ref
        var refsDir = Path.Combine(versionDir, $"refs.{suffix}");
        foreach (var table in excelTables)
        {
            await RefFileWriter.WriteAsync(
                table.GetSchema(),
                table.GetData(),
                Path.Combine(refsDir, $"{table.Name}.ref"),
                ct);
        }

        var refsFilePath = Path.Combine(versionDir, $"{fileId}.{suffix}.refs");
        await RefsPackWriter.WriteAsync(refsDir, refsFilePath, ct);
        
        // 파일 업로드
        await UploadIfExistsAsync($"patch/refs/{fileId}.{suffix}.schs", schsFilePath, ct);
        await UploadIfExistsAsync($"patch/refs/{fileId}.{suffix}.refs", refsFilePath, ct);
    }

    private async Task UploadIfExistsAsync(string key, string filePath, CancellationToken ct)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists) return;
        
        await using var stream = File.OpenRead(filePath);
        await _uploader.UploadAsync(key, stream, info.Length, ct);
    }

    private async Task<Dictionary<string, PatchTarget>> SyncAndLoadTargetAsync(
        IEnumerable<string> excelTableNames,
        CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);
        await conn.BeginTransactionAsync(ct);

        var targets = await ResourceDb.GetRefsTableTargetsAsync(conn, ct);
        
        var map = new Dictionary<string, PatchTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in targets)
            map[t.TableName] = (PatchTarget)t.TargetFlags;

        foreach (var name in excelTableNames)
        {
            if (map.ContainsKey(name))
                continue;

            await ResourceDb.UpsertRefsTableTargetAsync(
                conn,
                new ResourceDb.RefsTableTargetEntity
                {
                    TableName = name,
                    TargetFlags = (byte)PatchTarget.Both,
                    Comment = "auto created"
                }, ct);
            
            map[name] = PatchTarget.Both;
        }
        
        await conn.CommitAsync(ct);
        return map;
    }
    
    private async Task LoadExcelAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ExcelFolder))
        {
            await Toast.Make("Please specify a valid Excel folder path.", ToastDuration.Long).Show(ct);
            return;
        }

        foreach (var old in ExcelTables)
            old.PropertyChanged -= OnTableItemPropertyChanged;
        
        ExcelTables.Clear();

        var tables = await _excel.LoadFromFolderAsync(ExcelFolder, ct);
        foreach (var vm in tables.Select(table => new ExcelTableModel(table)))
        {
            vm.PropertyChanged += OnTableItemPropertyChanged;
            ExcelTables.Add(vm);
        }

        TotalTableCount = ExcelTables.Count;
        IsExcelLoaded = TotalTableCount > 0;
        
        UpdateCheckedCount();
        OnPropertyChanged(nameof(HasDirtyTargets));
    }

    private async Task LoadOriginAsync(CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);

        FileId = await ResourceDb.GetLatestRefsFileIdAsync(conn, ct) + 1;
            
        var files = await ResourceDb.GetRefsFilesAsync(conn, ct);
            
        OriginRefsFiles.Clear();
        foreach (var entity in files.OrderByDescending(entity => entity.FileId))
        {
            if (entity.IsDevelopment)
                continue;
                
            var file = new RefsFile(entity);
            OriginRefsFiles.Add(file);
        }

        SelectedOriginRefsFile = OriginRefsFiles.FirstOrDefault();
        IsOriginEnabled = OriginRefsFiles.Count > 0;
    }
    
    private async Task BrowseExcelFolderAsync()
    {
        if (!CanBrowseExcel) return;
        
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = cts.Token;

        try
        {
            IsBusy = true;

            var result = await _folderPicker.PickAsync(ct);
            if (!result.IsSuccessful || result.Folder is null)
                return;

            ExcelFolder = result.Folder.Path;

            await LoadExcelAsync(ct);

            var names = ExcelTables.Select(x => x.Name).ToList();
            var map = await SyncAndLoadTargetAsync(names, ct);
            foreach (var t in ExcelTables)
            {
                var target = map.GetValueOrDefault(t.Name, PatchTarget.Both);
                t.SetTarget(target);
            }

            await LoadOriginAsync(ct);
            
            IsAllChecked = true;
            IsAutoComment = true;
            IsGenerateCode = true;
            if (IsAutoComment) Comment = BuildAutoComment();

            await Toast.Make(IsExcelLoaded
                ? $"Excel '{TotalTableCount}' files loaded."
                : "Excel files not loaded.", ToastDuration.Long).Show(ct);
        }
        catch (Exception e)
        {
            await Utils.AlertAsync(AlertLevel.Error, $"Failed to load excel file: {e.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BrowseOutputFolderAsync()
    {
        if (!CanBrowseOutput) return;
        
        var result = await _folderPicker.PickAsync();
        if (!result.IsSuccessful || result.Folder is null)
            return;
        
        OutputFolder = result.Folder.Path;
        IsOutputSelected = true;
        
        RefreshUI();
    }
    
    private void OnTableItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ExcelTableModel.IsChecked):
                UpdateCheckedCount();
                break;
            case nameof(ExcelTableModel.Target) or nameof(ExcelTableModel.IsTargetDirty):
                OnPropertyChanged(nameof(HasDirtyTargets));
                break;
        }
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
    
    private string BuildAutoComment()
    {
        var selected = ExcelTables
            .Where(x => x.IsChecked)
            .Select(x => x.Name)
            .ToList();
        
        if (selected.Count == 0)
            return string.Empty;

        const int maxNames = 10;
        return selected.Count <= maxNames 
            ? string.Join(", ", selected) 
            : $"{string.Join(", ", selected.Take(maxNames))} ... (+{selected.Count - maxNames}";
    }

    private void RefreshUI()
    {
        BrowseExcelCommand.RaiseCanExecuteChanged();
        BrowseOutputCommand.RaiseCanExecuteChanged();
        GenerateCommand.RaiseCanExecuteChanged();
        
        OnPropertyChanged(nameof(CanBrowseExcel));
        OnPropertyChanged(nameof(CanBrowseOutput));
        OnPropertyChanged(nameof(CanGenerate));
    }
}
