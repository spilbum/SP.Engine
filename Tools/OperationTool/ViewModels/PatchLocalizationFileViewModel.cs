using System.Collections.ObjectModel;
using CommunityToolkit.Maui.Alerts;
using OperationTool.DatabaseHandler;
using OperationTool.Services;
using SP.Shared.Resource;

namespace OperationTool.ViewModels;

public class PatchLocalizationFileViewModel : ViewModelBase
{
    private readonly IDbConnector _db;
    private readonly ResourceServerWebService _web;

    private CancellationTokenSource? _cts;
    
    private bool _isBusy;
    private ServerGroupType _serverGroupType;
    private StoreType _storeType;
    private LocalizationFile? _selectedFile;
    private LocalizationActive? _currentActive;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            RefreshUI();
        }
    }

    public ObservableCollection<ServerGroupType> ServerGroupTypes { get; } = [];
    public ObservableCollection<StoreType> StoreTypes { get; } = [];
    public ObservableCollection<LocalizationFile> Files { get; } = [];

    public ServerGroupType ServerGroupType
    {
        get => _serverGroupType;
        set
        {
            if (!SetProperty(ref _serverGroupType, value)) return;
            _ = LoadActiveAsync();
            RefreshUI();
        }
    }

    public StoreType StoreType
    {
        get => _storeType;
        set
        {
            if (!SetProperty(ref _storeType, value)) return;
            _ = LoadActiveAsync();
            RefreshUI();
        }
    }

    public LocalizationFile? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (!SetProperty(ref _selectedFile, value)) return;
            RefreshUI();
        }
    }

    public LocalizationActive? CurrentActive
    {
        get => _currentActive;
        private set
        {
            if (!SetProperty(ref _currentActive, value)) return;
            OnPropertyChanged(nameof(HasCurrentActive));
            OnPropertyChanged(nameof(CurrentActiveFileIdText));
            OnPropertyChanged(nameof(CurrentActiveUpdatedText));
        }
    }
    
    public bool HasCurrentActive => CurrentActive is not null;
    public string CurrentActiveFileIdText => CurrentActive?.FileId.ToString() ?? "_";
    public string CurrentActiveUpdatedText => CurrentActive?.UpdatedUtcText ?? "_";

    public bool CanApply =>
        !IsBusy &&
        ServerGroupType != ServerGroupType.None &&
        StoreType != StoreType.None &&
        SelectedFile is not null;
    
    public AsyncRelayCommand ApplyCommand { get; }

    public PatchLocalizationFileViewModel(IDbConnector db, ResourceServerWebService web)
    {
        _db = db;
        _web = web;

        foreach (var v in Enum.GetValues<ServerGroupType>())
        {
            if (v == ServerGroupType.None) continue;
            ServerGroupTypes.Add(v);
        }
        
        foreach (var v in Enum.GetValues<StoreType>())
        {
            if (v == StoreType.None) continue;
            StoreTypes.Add(v);
        }
        
        _serverGroupType = ServerGroupTypes.FirstOrDefault();
        _storeType = StoreTypes.FirstOrDefault();

        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => CanApply);
    }

    private void RefreshUI()
    {
        ApplyCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanApply));
    }

    public async Task LoadAsync()
    {
        await ReloadAsync();
        await LoadActiveAsync();
    }

    private async Task ReloadAsync()
    {
        if (IsBusy) return;

        var ct = ResetCts(TimeSpan.FromMinutes(1));

        try
        {
            IsBusy = true;

            using var conn = await _db.OpenAsync(ct);
            var list = await ResourceDb.GetLocalizationFilesAsync(conn, ct);

            Files.Clear();
            foreach (var entry in list.OrderByDescending(x => x.FileId))
                Files.Add(new LocalizationFile(entry));

            SelectedFile = Files.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            await Utils.AlertAsync(AlertLevel.Error, $"Reload filed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadActiveAsync()
    {
        if (ServerGroupType == ServerGroupType.None || StoreType == StoreType.None)
        {
            CurrentActive = null;
            return;
        }
        
        var ct = ResetCts(TimeSpan.FromMinutes(1));

        try
        {
            using var conn = await _db.OpenAsync(ct);
            var list = await ResourceDb.GetLocalizationActivesAsync(conn, ct);
            var entity = list.FirstOrDefault(x =>
                x.ServerGroupType == ServerGroupType.ToString() && x.StoreType == StoreType.ToString());
            CurrentActive = entity is null ? null : new LocalizationActive(entity);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch
        {
            CurrentActive = null;
        }
    }

    private async Task ApplyAsync()
    {
        if (!CanApply) return;
        
        var ok = await Shell.Current.DisplayAlert(
            "Confirm",
            $"Apply localization?\n\nTarget: {ServerGroupType}/{StoreType}\nFileId: {SelectedFile!.FileId}",
            "Ok",
            "Cancel");

        if (!ok) return;
        
        var ct = ResetCts(TimeSpan.FromMinutes(1));

        try
        {
            IsBusy = true;

            using var conn = await _db.OpenAsync(ct);
            await ResourceDb.UpsertLocalizationActiveAsync(
                conn,
                new ResourceDb.LocalizationActiveEntity
                {
                    ServerGroupType = ServerGroupType.ToString(),
                    StoreType = StoreType.ToString(),
                    FileId = SelectedFile!.FileId
                }, ct);

            try
            {
                await _web.RefreshAsync(ct);
            }
            catch (Exception e)
            {
                await Utils.AlertAsync(AlertLevel.Error, $"Applied in DB, but notify failed: {e.Message}");
            }

            await Toast.Make("Applied.").Show(ct);
            await Shell.Current.GoToAsync("..");
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception e)
        {
            await Utils.AlertAsync(AlertLevel.Error, $"Apply failed: {e.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
    
    private CancellationToken ResetCts(TimeSpan timeout)
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        _cts?.Dispose();
        _cts = new CancellationTokenSource(timeout);
        return _cts.Token;
    }
}
