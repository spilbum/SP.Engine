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

public sealed class GenerateFileViewModel : ViewModelBase
{
    private readonly IDialogService _dialog;
    private readonly IExcelService _excel;
    private readonly IDbConnector _db;
    private readonly IFolderPicker _folderPicker;
    private readonly IFileUploader _uploader;

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
    private bool _isAutoComment;
    private bool _isGenerateCode;
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
        set
        {
            if (!SetProperty(ref _isAutoComment, value)) return;
            if (value) Comment = string.Join(",", ExcelTables.Select(x => x.Name));
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

    public bool HasDirtyTargets => ExcelTables.Any(x => x.IsTargetDirty);

    public ObservableCollection<ExcelTableModel> ExcelTables { get; } = [];
    public ObservableCollection<RefsFileModel> OriginRefsFiles { get; } = [];
    public AsyncRelayCommand BrowseExcelFolderCommand { get; }
    public AsyncRelayCommand BrowseOutputFolderCommand { get; }
    public AsyncRelayCommand GenerateCommand { get; }

    public GenerateFileViewModel(
        IDialogService dialog,
        IExcelService excel,
        IDbConnector db,
        IFolderPicker folderPicker,
        IFileUploader uploader)
    {
        _dialog = dialog;
        _excel = excel;
        _db = db;
        _folderPicker = folderPicker;
        _uploader = uploader;
        
        BrowseExcelFolderCommand = new AsyncRelayCommand(BrowseExcelFolderAsync);
        BrowseOutputFolderCommand = new AsyncRelayCommand(BrowseOutputFolderAsync);
        GenerateCommand = new AsyncRelayCommand(GenerateAsync, CanGenerate);
    }

    private async Task EnsureTargetsSaveAsync(DbConn conn, CancellationToken ct)
    {
        var dirty = ExcelTables.Where(x => x.IsTargetDirty).ToList();
        if (dirty.Count == 0) return;

        foreach (var t in dirty)
        {
            await ResourceDb.UpsertResourceTableTargetAsync(
                conn,
                new ResourceDb.ResourceTableTargetEntity
                {
                    TableName = t.Name,
                    Target = (byte)t.Target,
                    Comment = "auto saved by generate"
                }, ct);
            
            t.MarkTargetSaved();
        }
        
        OnPropertyChanged(nameof(HasDirtyTargets));
    }

    private async Task GenerateAsync()
    {
        var ok = await Shell.Current.DisplayAlert(
            "",
            "Would you like to generate a file?",
            "Ok",
            "Cancel");
        
        if (!ok)
            return;
        
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = cts.Token;
        
        try
        {
            using var conn = await _db.OpenAsync(ct);
            await conn.BeginTransactionAsync(ct);

            await EnsureTargetsSaveAsync(conn, ct);
            
            var entity = new ResourceDb.ResourceRefsFileEntity
            {
                FileId = FileId,
                Comment = Comment,
                IsDevelopment = IsDevelopment
            };

            await ResourceDb.InsertResourceRefsFileAsync(conn, entity, ct);

            var versionDir = Path.Combine(OutputFolder, $"{FileId}");
            var selected = ExcelTables
                .Where(table => table.IsChecked)
                .ToList();

            var clientTables = selected.Where(t => (t.Target & PatchTarget.Client) != 0).ToList();
            var serverTables = selected.Where(t => (t.Target & PatchTarget.Server) != 0).ToList();
            
            await BuildAndUploadAsync(FileId, PatchDeliveryTarget.Client, clientTables, versionDir, ct);
            await BuildAndUploadAsync(FileId, PatchDeliveryTarget.Server, serverTables, versionDir, ct);
         
            // {table}.cs 파일 생성
            var codeDir = IsDevelopment
                ? Path.Combine(versionDir, "code", "dev")
                : Path.Combine(versionDir, "code");

            if (IsGenerateCode)
            {
                var schemas = selected.Select(t => t.GetSchema()).ToList();
                ReferenceCodeGenerator.Generate(schemas, codeDir, "SP.Shared.Resource");
            }
            
            await conn.CommitAsync(ct);
            await Utils.OpenFolderAsync(versionDir);
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception e)
        {
            await _dialog.AlertAsync("Error", $"Generate failed: {e.Message}");
        }
    }

    private async Task BuildAndUploadAsync(
        int fileId,
        PatchDeliveryTarget target,
        List<ExcelTableModel> excelTables,
        string versionDir,
        CancellationToken ct)
    {
        if (excelTables.Count == 0)
            return;

        var suffix = target.ToSuffix();

        const string schExt = "sch";
        const string refExt = "ref";
        
        var schsExt = PatchFileKind.Schs.ToExt();
        var refsExt = PatchFileKind.Refs.ToExt();
        
        // .sch
        var schsDir = Path.Combine(versionDir, $"{schsExt}.{suffix}");
        foreach (var table in excelTables)
        {
            await SchFileWriter.WriteAsync(
                table.GetSchema(),
                Path.Combine(schsDir, $"{table.Name}.{schExt}"),
                ct);
        }

        var schsFilePath = Path.Combine(versionDir, $"{fileId}.{suffix}.{schsExt}");
        await SchsPackWriter.WriteAsync(schsDir, schsFilePath, ct);

        // .ref
        var refsDir = Path.Combine(versionDir, $"{refsExt}.{suffix}");
        foreach (var table in excelTables)
        {
            await RefFileWriter.WriteAsync(
                table.GetSchema(),
                table.GetData(),
                Path.Combine(refsDir, $"{table.Name}.{refExt}"),
                ct);
        }

        var refsFilePath = Path.Combine(versionDir, $"{fileId}.{suffix}.{refsExt}");
        await RefsPackWriter.WriteAsync(refsDir, refsFilePath, ct);
        
        // 파일 업로드
        await UploadIfExistsAsync($"patch/{fileId}.{suffix}.{schsExt}", schsFilePath, ct);
        await UploadIfExistsAsync($"patch/{fileId}.{suffix}.{refsExt}", refsFilePath, ct);
    }

    private async Task UploadIfExistsAsync(string key, string filePath, CancellationToken ct)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists)
            return;
        
        await using var stream = File.OpenRead(filePath);
        await _uploader.UploadAsync(key, stream, info.Length, ct);
    }

    private async Task<Dictionary<string, PatchTarget>> SyncAndLoadTargetAsync(
        IEnumerable<string> excelTableNames,
        CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);
        await conn.BeginTransactionAsync(ct);

        var targets = await ResourceDb.GetResourceTableTargetsAsync(conn, ct);
        
        var map = new Dictionary<string, PatchTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in targets)
            map[t.TableName] = (PatchTarget)t.Target;

        foreach (var name in excelTableNames)
        {
            if (map.ContainsKey(name))
                continue;

            await ResourceDb.UpsertResourceTableTargetAsync(
                conn,
                new ResourceDb.ResourceTableTargetEntity
                {
                    TableName = name,
                    Target = (byte)PatchTarget.Shared,
                    Comment = "auto created"
                }, ct);
            
            map[name] = PatchTarget.Shared;
        }
        
        await conn.CommitAsync(ct);
        return map;
    }

    private void ApplyTargetsToModels(Dictionary<string, PatchTarget> map)
    {
        foreach (var t in ExcelTables)
        {
            var target = map.GetValueOrDefault(t.Name, PatchTarget.Shared);
            t.SetTarget(target);
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
        
        ExcelTables.Clear();

        var tables = await _excel.LoadFromFolderAsync(ExcelFolder, ct);
        foreach (var vm in tables.Select(table => new ExcelTableModel(table)))
        {
            vm.PropertyChanged += OnTableItemPropertyChanged;
            ExcelTables.Add(vm);
        }

        TotalTableCount = ExcelTables.Count;
        IsExcelLoaded = TotalTableCount > 0;
    }

    private async Task LoadOriginAsync(CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);

        FileId = await ResourceDb.GetLatestResourceRefsFileIdAsync(conn, ct) + 1;
            
        var files = await ResourceDb.GetResourceRefsFilesAsync(conn, ct);
            
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
    
    private async Task BrowseExcelFolderAsync()
    {
        
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        try
        {
            var result = await _folderPicker.PickAsync(ct);
            if (!result.IsSuccessful || result.Folder is null)
                return;

            ExcelFolder = result.Folder.Path;
            
            await LoadExcelAsync(ct);
            
            var names = ExcelTables.Select(x => x.Name).ToList();
            var targetMap = await SyncAndLoadTargetAsync(names, ct);
            ApplyTargetsToModels(targetMap);
            
            await LoadOriginAsync(ct);

            IsAutoComment = true;
            IsGenerateCode = true;
            
            await Toast.Make(IsExcelLoaded ? $"Excel '{TotalTableCount}' files loaded." : "Excel files not loaded.", ToastDuration.Long).Show(ct);
        }
        catch (Exception e)
        {
            await _dialog.AlertAsync("Error", $"Failed to load excel file: {e.Message}");
        }
    }

    private async Task BrowseOutputFolderAsync()
    {
        var result = await _folderPicker.PickAsync();
        if (!result.IsSuccessful || result.Folder is null)
            return;
        
        OutputFolder = result.Folder.Path;
        IsOutputSelected = true;
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
}
