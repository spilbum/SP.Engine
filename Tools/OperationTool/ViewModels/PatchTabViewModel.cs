using System.Collections.ObjectModel;
using OperationTool.DatabaseHandler;
using OperationTool.Pages;
using OperationTool.Services;
using SP.Shared.Resource;

namespace OperationTool.ViewModels;

public sealed class RefsPatchVersion(ResourceDb.RefsPatchVersionEntity e)
{
    public string ServerGroupType { get; } = e.ServerGroupType;
    public int PatchVersion { get; } = e.PatchVersion;
    public int TargetMajor { get; } = e.TargetMajor;
    public int FileId { get; } = e.FileId;
    public string? Comment { get; } = e.Comment;
    public string CreatedUtcText { get; } = e.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss");
}

public sealed class PatchTabViewModel : ViewModelBase
{
    private readonly IDbConnector _db;
    private ServerGroupType _selectedServerGroupType;
    private CancellationTokenSource? _cts;

    public ServerGroupType SelectedServerGroupType
    {
        get => _selectedServerGroupType;
        set
        {
            if (SetProperty(ref _selectedServerGroupType, value))
                _ = LoadAsync();
        }
    }
    
    public ObservableCollection<RefsPatchVersion> RefsPatchVersions { get; } = [];
    public ObservableCollection<ServerGroupType> ServerGroupTypes { get; } = [];
    
    public AsyncRelayCommand GoToGenerateRefsFileCommand { get; }
    public AsyncRelayCommand GoToPatchRefsFileCommand { get; }
    public AsyncRelayCommand GoToRefsDiffCommand { get; }

    public PatchTabViewModel(IDbConnector db)
    {
        _db = db;

        foreach (ServerGroupType serverGroupType in Enum.GetValues(typeof(ServerGroupType)))
        {
            if (serverGroupType == ServerGroupType.None) continue;
            ServerGroupTypes.Add(serverGroupType);
        }
        
        SelectedServerGroupType = ServerGroupTypes.FirstOrDefault();
        
        GoToGenerateRefsFileCommand = new AsyncRelayCommand(GoToGenerateRefsFileAsync);
        GoToPatchRefsFileCommand = new AsyncRelayCommand(GoToPatchRefsFileAsync);
        GoToRefsDiffCommand = new AsyncRelayCommand(GoToRefsDiffAsync);
    }
    
    private static async Task GoToGenerateRefsFileAsync()
        => await Utils.GoToPageAsync(nameof(GenerateRefsFilePage));
    
    private static async Task GoToPatchRefsFileAsync()
        => await Utils.GoToPageAsync(nameof(PatchRefsFilePage));
    
    private static async Task GoToRefsDiffAsync()
        => await Utils.GoToPageAsync(nameof(RefsDiffTabPage));

    public async Task LoadAsync()
    {
        var ct = ResetCts(TimeSpan.FromMinutes(1));

        try
        {
            using var conn = await _db.OpenAsync(ct);
            var versions = await ResourceDb.GetRefsPatchVersions(
                conn, 
                SelectedServerGroupType, 
                ct);
            
            RefsPatchVersions.Clear();
            foreach (var entity in versions.OrderByDescending(f => f.PatchVersion).Take(50))
            {
                RefsPatchVersions.Add(new RefsPatchVersion(entity));
            }
        }
        catch (Exception e)
        {
            await Utils.AlertAsync(AlertLevel.Error, $"Load failed: {e.Message}");
        }
    }

    private CancellationToken ResetCts(TimeSpan timeout)
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        _cts = new CancellationTokenSource(timeout);
        return _cts.Token;
    }
}
