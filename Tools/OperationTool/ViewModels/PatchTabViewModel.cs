using System.Collections.ObjectModel;
using OperationTool.DatabaseHandler;
using OperationTool.Models;
using OperationTool.Pages;
using OperationTool.Services;
using SP.Shared.Resource;

namespace OperationTool.ViewModels;

public sealed class PatchTabViewModel : ViewModelBase
{
    private readonly IDialogService _dialog;
    private readonly IDbConnector _db;
    private ServerGroupType _selectedServerGroupType;

    public ServerGroupType SelectedServerGroupType
    {
        get => _selectedServerGroupType;
        set
        {
            if (SetProperty(ref _selectedServerGroupType, value))
                _ = LoadResourcePatchVersionsAsync(value);
        }
    }
    
    public ObservableCollection<ResourcePatchVersionModel> ResourcePatchVersions { get; } = [];
    public ObservableCollection<ServerGroupType> ServerGroupTypes { get; } = [];
    
    public AsyncRelayCommand GoToGenerateFileCommand { get; }
    public AsyncRelayCommand GoToRunPathCommand { get; }
    public AsyncRelayCommand ReloadCommand { get; }

    public PatchTabViewModel(IDialogService dialog, IDbConnector db)
    {
        _dialog = dialog;
        _db = db;

        foreach (ServerGroupType serverGroupType in Enum.GetValues(typeof(ServerGroupType)))
        {
            if (serverGroupType == ServerGroupType.None) continue;
            ServerGroupTypes.Add(serverGroupType);
        }
        
        SelectedServerGroupType = ServerGroupTypes.FirstOrDefault();
        
        GoToGenerateFileCommand = new AsyncRelayCommand(GoToGenerateFileAsync);
        GoToRunPathCommand = new AsyncRelayCommand(GoToRunPathAsync);
        ReloadCommand = new AsyncRelayCommand(ReloadAsync);
    }
    
    private async Task GoToGenerateFileAsync()
        => await Utils.GoToPageAsync(nameof(GenerateFilePage));
    
    private async Task GoToRunPathAsync()
        => await Utils.GoToPageAsync(nameof(RunPatchPage));

    private async Task ReloadAsync()   
        => await LoadResourcePatchVersionsAsync(SelectedServerGroupType);
    
    private async Task LoadResourcePatchVersionsAsync(ServerGroupType serverGroupType)
    {
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        try
        {
            using var conn = await _db.OpenAsync(ct);
            var entities = await ResourceDb.GetLatestResourcePatchVersions(
                conn, serverGroupType, 100, ct);
            
            ResourcePatchVersions.Clear();
            foreach (var entity in entities)
            {
                ResourcePatchVersions.Add(new ResourcePatchVersionModel(entity));
            }
        }
        catch (Exception e)
        {
            await _dialog.AlertAsync("Error", $"Load failed: {e.Message}");
        }
    }
}
