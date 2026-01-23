using System.Collections.ObjectModel;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Storage;
using OperationTool.DatabaseHandler;
using OperationTool.Localization;
using OperationTool.Services;
using SP.Shared.Resource;

namespace OperationTool.ViewModels;

public sealed class GenerateLocalizationFileViewModel : ViewModelBase
{
    private readonly IFilePicker _filePicker;
    private readonly IFolderPicker _folderPicker;
    private readonly ILocalizationService _localization;
    private readonly IDbConnector _db;
    private readonly IFileUploader _fileUploader;
    private string _excelFilePath = string.Empty;
    private string _outputFolder = string.Empty;
    private bool _isExcelSelected;
    private bool _isOutputSelected;
    private bool _isBusy;
    private int _fileId;
    private int _totalKeys;
    private string? _comment;
    private LocalizationParseResult? _parsed;

    private CancellationTokenSource? _cts;

    public int FileId
    {
        get => _fileId;
        set
        {
            if (SetProperty(ref _fileId, value))
                GenerateCommand.RaiseCanExecuteChanged();
        }
    }

    public string? Comment
    {
        get => _comment;
        set => SetProperty(ref _comment, value);
    }

    public int TotalKeys
    {
        get => _totalKeys;
        set => SetProperty(ref _totalKeys, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            BrowseExcelCommand.RaiseCanExecuteChanged();
            BrowseOutputCommand.RaiseCanExecuteChanged();
            GenerateCommand.RaiseCanExecuteChanged();
            
            OnPropertyChanged(nameof(CanBrowseExcel));
            OnPropertyChanged(nameof(CanBrowseOutput));
            OnPropertyChanged(nameof(CanGenerate));
        }
    }

    public string ExcelFilePath
    {
        get => _excelFilePath; 
        set => SetProperty(ref _excelFilePath, value);
    }

    public string OutputFolder
    {
        get => _outputFolder; 
        set => SetProperty(ref _outputFolder, value);
    }

    public bool IsExcelSelected
    {
        get => _isExcelSelected;
        private set
        {
            if (SetProperty(ref _isExcelSelected, value))
                GenerateCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsOutputSelected
    {
        get => _isOutputSelected;
        private set
        {
            if (SetProperty(ref _isOutputSelected, value))
                GenerateCommand.RaiseCanExecuteChanged();
        }
    }

    public ObservableCollection<string> Languages { get; } = [];

    public bool CanBrowseExcel => !IsBusy;
    public bool CanBrowseOutput => !IsBusy;
    public bool CanGenerate => 
        !IsBusy && 
        IsExcelSelected && 
        IsOutputSelected &&
        _parsed is not null &&
        FileId > 0;
    
    public AsyncRelayCommand BrowseExcelCommand { get; }
    public AsyncRelayCommand BrowseOutputCommand { get; }
    public AsyncRelayCommand GenerateCommand { get; }

    public GenerateLocalizationFileViewModel(
        IFilePicker filePicker,
        IFolderPicker folderPicker,
        ILocalizationService localization,
        IDbConnector db,
        IFileUploader fileUploader)
    {
        _filePicker = filePicker;
        _folderPicker = folderPicker;
        _localization = localization;
        _db = db;
        _fileUploader = fileUploader;

        BrowseExcelCommand = new AsyncRelayCommand(BrowseExcelAsync, () => CanBrowseExcel); 
        BrowseOutputCommand = new AsyncRelayCommand(BrowseOutputAsync, () => CanBrowseOutput);
        GenerateCommand = new AsyncRelayCommand(GenerateAsync, () => CanGenerate);
    }

    public async Task LoadAsync()
    {
        if (IsBusy) return;

        var ct = ResetCts(TimeSpan.FromSeconds(30));
        
        try
        {
            IsBusy = true;
            
            using var conn = await _db.OpenAsync(ct);
            var fileId = await ResourceDb.GetLatestLocalizationFileIdAsync(conn, ct);
            FileId = fileId + 1;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BrowseExcelAsync()
    {
        if (!CanBrowseExcel) return;

        var ct = ResetCts(TimeSpan.FromSeconds(30));

        try
        {
            IsBusy = true;

            var result = await _filePicker.PickAsync();
            if (string.IsNullOrEmpty(result?.FileName))
                return;

            if (!Utils.ValidateExtension(result, "xlsx"))
            {
                await Toast.Make("Only XLSX files can be selected.").Show(ct);
                return;
            }

            ExcelFilePath = result.FullPath;

            _parsed = await _localization.ParseAsync(ExcelFilePath, ct);
            ApplyParsed(_parsed);

            await Toast.Make("Localization excel loaded.", ToastDuration.Long).Show(ct);
        }
        catch (Exception e)
        {
            ClearParsed();
            await Utils.AlertAsync(AlertLevel.Error, $"Failed to load/parse excel: {e.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyParsed(LocalizationParseResult parsed)
    {
        TotalKeys = parsed.TotalKeys;
        
        Languages.Clear();
        foreach (var lang in parsed.Languages)
            Languages.Add(lang);
        
        IsExcelSelected = true;
        
        GenerateCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanGenerate));
    }

    private void ClearParsed()
    {
        _parsed = null;
        TotalKeys = 0;
        Languages.Clear();
        IsExcelSelected = false;
        
        GenerateCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanGenerate));
    }

    private async Task BrowseOutputAsync()
    {
        var ct = ResetCts(TimeSpan.FromSeconds(30));

        try
        {
            IsBusy = true;

            var result = await _folderPicker.PickAsync(ct);
            if (!result.IsSuccessful || result.Folder is null)
                return;

            OutputFolder = result.Folder.Path;
            IsOutputSelected = true;

            await Toast.Make("Output folder selected.", ToastDuration.Long).Show(ct);
        }
        catch (Exception e)
        {
            await Utils.AlertAsync(AlertLevel.Error, $"Failed to select output folder: {e.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task GenerateAsync()
    {
        if (!CanGenerate) return;
        if (_parsed is null) return;
        
        var ok = await Shell.Current.DisplayAlert(
            "",
            $"Generate localization pack?\n\nFileId: {FileId}\nKeys: {TotalKeys}\nLang: {string.Join(", ", Languages)}",
            "Ok",
            "Cancel");

        if (!ok)
            return;

        var ct = ResetCts(TimeSpan.FromMinutes(1));

        try
        {
            IsBusy = true;
            
            var filePath = await _localization.GenerateLocsFileAsync(_parsed, FileId, OutputFolder, ct);
            
            using var conn = await _db.OpenAsync(ct);
            await conn.BeginTransactionAsync(ct);
            
            await ResourceDb.InsertLocalizationFileAsync(
                conn,
                new ResourceDb.LocalizationFileEntity
                {
                    FileId = FileId,
                    Comment = Comment
                }, ct);

            await UploadIfExistsAsync(PatchUtil.BuildLocalizationUploadKey(FileId, PatchConst.LocsFile), filePath, ct);
            
            await conn.CommitAsync(ct);
            
            await Utils.AlertAsync(AlertLevel.Info, $"Generated: {filePath}");
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
    
    private async Task UploadIfExistsAsync(string key, string filePath, CancellationToken ct)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists) return;
        
        await using var stream = File.OpenRead(filePath);
        await _fileUploader.UploadAsync(key, stream, info.Length, ct);
    }

    private CancellationToken ResetCts(TimeSpan timeout)
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        _cts = new CancellationTokenSource(timeout);
        return _cts.Token;
    }
}
