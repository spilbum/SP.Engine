using System.Collections.ObjectModel;
using CommunityToolkit.Maui.Alerts;
using OperationTool.DatabaseHandler;
using OperationTool.Services;
using SP.Shared.Resource;
using SP.Shared.Resource.Web;

namespace OperationTool.ViewModels;

public sealed class PatchRefsFileViewModel : ViewModelBase
{
    private readonly IDbConnector _db;
    private readonly ResourceServerWebService _web;

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _execCts;

    private bool _isBusy;
    
    private RefsFile? _selectedRefsFile;
    private ServerGroupType _selectedServerGroupType;
    private int _patchVersion;
    private int _selectedTargetMajor;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            RefreshUI();
            OnPropertyChanged(nameof(CanExecute));
        }
    }

    public bool HasSelectedRefsFile => SelectedRefsFile is not null;
    public string SelectedRefsFileComment => SelectedRefsFile?.Comment ?? string.Empty;
    public bool SelectedRefsFileIsDevelopment => SelectedRefsFile?.IsDevelopment ?? false;
    
    public int SelectedTargetMajor
    {
        get => _selectedTargetMajor;
        set
        {
            if (!SetProperty(ref _selectedTargetMajor, value)) return;
            RefreshUI();
            OnPropertyChanged(nameof(CanExecute));
        }
    }

    public int PatchVersion
    {
        get => _patchVersion;
        private set => SetProperty(ref _patchVersion, value);
    }

    public RefsFile? SelectedRefsFile
    {
        get => _selectedRefsFile;
        set
        {
            if (!SetProperty(ref _selectedRefsFile, value)) return;
            OnPropertyChanged(nameof(HasSelectedRefsFile));
            OnPropertyChanged(nameof(SelectedRefsFileComment));
            OnPropertyChanged(nameof(SelectedRefsFileIsDevelopment));
            RefreshUI();
            OnPropertyChanged(nameof(CanExecute));
        }
    }
    
    public ServerGroupType SelectedServerGroupType
    {
        get => _selectedServerGroupType;
        set
        {
            if (!SetProperty(ref _selectedServerGroupType, value)) return;
            _ = LoadAsync();
            RefreshUI();
            OnPropertyChanged(nameof(CanExecute));
        }
    }

    public ObservableCollection<ServerGroupType> ServerGroupTypes { get; } = [];
    public ObservableCollection<RefsFile> RefsFiles { get; } = [];
    public ObservableCollection<int> TargetMajors { get; } = [];
    public AsyncRelayCommand ExecuteCommand { get; }

    public PatchRefsFileViewModel(
        IDbConnector db, 
        ResourceServerWebService web)
    {
        _db = db;
        _web = web;

        foreach (ServerGroupType t in Enum.GetValues(typeof(ServerGroupType)))
        {
            if (t == ServerGroupType.None) continue;
            ServerGroupTypes.Add(t);
        }
        
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => CanExecute);
        
        _selectedServerGroupType = ServerGroupTypes.FirstOrDefault();
    }
    
    public bool CanExecute =>
        !IsBusy
        && SelectedServerGroupType != ServerGroupType.None
        && SelectedRefsFile is not null
        && SelectedTargetMajor > 0
        && PatchVersion > 0;

    private async Task ExecuteAsync()
    {
        if (!CanExecute) return;
        
        var confirm = await Shell.Current.DisplayAlert(
            "Confirm",
            "Would you like to run the patch?",
            "Ok",
            "Cancel");
        
        if (!confirm)
            return;
        
        CancelExecute();
        _execCts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = _execCts.Token;

        try
        {
            IsBusy = true;

            using var conn = await _db.OpenAsync(ct);

            await ResourceDb.InsertRefsPatchVersionAsync(
                conn, 
                new ResourceDb.RefsPatchVersionEntity
                {
                    ServerGroupType = SelectedServerGroupType.ToString(),
                    PatchVersion = PatchVersion,
                    TargetMajor = SelectedTargetMajor,
                    FileId = SelectedRefsFile!.FileId,
                    Comment = SelectedRefsFile!.Comment,
                    CreatedUtc = DateTime.UtcNow
                }, 
                ct);

            try
            {
                await _web.RefreshAsync(ct);
                await Toast.Make("Patch notification sent").Show(ct);
            }
            catch (RpcException e)
            {
                await Utils.AlertAsync(AlertLevel.Error,
                    $"Patch version saved, but notify failed: {e.Message}");
            }

            await Toast.Make("Execute patch successfully").Show(ct);
            await Utils.GoToPageAsync("..");
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception e)
        {
            await Utils.AlertAsync(AlertLevel.Error, $"Failed to execute patch: {e.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadAsync()
    {
        CancelLoad();
        _loadCts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = _loadCts.Token;

        try
        {
            IsBusy = true;

            using var conn = await _db.OpenAsync(ct);

            var files = await ResourceDb.GetRefsFilesAsync(conn, ct);

            RefsFiles.Clear();
            foreach (var entity in files.OrderByDescending(e => e.FileId))
                RefsFiles.Add(new RefsFile(entity));

            SelectedRefsFile = RefsFiles.FirstOrDefault();

            PatchVersion = await ResourceDb.GetLatestPatchVersionAsync(conn, SelectedServerGroupType, ct) + 1;

            var versions = await ResourceDb.GetClientBuildVersions(conn, ct);

            var beginMajor = int.MaxValue;
            var endMajor = 0;

            TargetMajors.Clear();
            foreach (var entity in versions)
            {
                if (!Enum.TryParse(entity.ServerGroupType, out ServerGroupType t) || t != SelectedServerGroupType)
                    continue;

                if (!BuildVersion.TryParse(entity.BeginBuildVersion, out var begin) ||
                    !BuildVersion.TryParse(entity.EndBuildVersion, out var end))
                    continue;

                if (beginMajor > begin.Major) beginMajor = begin.Major;
                if (endMajor < end.Major) endMajor = end.Major;
            }

            TargetMajors.Clear();
            if (endMajor > 0 && beginMajor != int.MaxValue)
            {
                for (var v = endMajor; v >= beginMajor; v--)
                    TargetMajors.Add(v);
            }

            SelectedTargetMajor = TargetMajors.FirstOrDefault();
        }
        catch (Exception e)
        {
            await Utils.AlertAsync(AlertLevel.Error, $"Load failed: {e.Message}");
        }
        finally
        {
            IsBusy = false;
            RefreshUI();
        }
    }

    private void RefreshUI()
    {
        ExecuteCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanExecute));
    }

    private void CancelLoad()
    {
        try
        {
            _loadCts?.Cancel();
        }
        catch
        {
            /* ignore */
        }
        finally
        {
            _loadCts?.Dispose();
            _loadCts = null;
        }
    }

    private void CancelExecute()
    {
        try
        {
            _execCts?.Cancel();
        }
        catch
        {
            /* ignore */
        }
        finally
        {
            _execCts?.Dispose();
            _execCts = null;
        }
    }
}
