using System.Collections.ObjectModel;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using OperationTool.DatabaseHandler;
using OperationTool.Models;
using OperationTool.Pages;
using SP.Shared.Resource;

namespace OperationTool.ViewModels;



public sealed class PatchTabViewModel : ViewModelBase
{
    private readonly IDbConnector _dbConnector;
    private ServerGroupType _selectedServerGroupType;

    public ServerGroupType SelectedServerGroupType
    {
        get => _selectedServerGroupType;
        set
        {
            if (SetProperty(ref _selectedServerGroupType, value))
            {
                _ = LoadResourcePatchVersionsAsync(value);
            }
        }
    }
    
    public ObservableCollection<ResourcePatchVersionModel> ResourcePatchVersions { get; } = [];
    public ObservableCollection<ServerGroupType> ServerGroupTypes { get; } = [];
    
    public AsyncRelayCommand GoToGenerateFileCommand { get; }
    public AsyncRelayCommand GoToRunPathCommand { get; }
    public AsyncRelayCommand ReloadCommand { get; }

    public PatchTabViewModel(IDbConnector dbConnector)
    {
        _dbConnector = dbConnector;

        foreach (ServerGroupType serverGroupType in Enum.GetValues(typeof(ServerGroupType)))
        {
            ServerGroupTypes.Add(serverGroupType);
        }
        SelectedServerGroupType = ServerGroupTypes.FirstOrDefault();
        
        GoToGenerateFileCommand = new AsyncRelayCommand(NavigateToGenerateFilePageAsync);
        GoToRunPathCommand = new AsyncRelayCommand(NavigateToRunPathPageAsync);
        ReloadCommand = new AsyncRelayCommand(ReloadAsync);
    }
    
    private async Task NavigateToGenerateFilePageAsync(object? state)
        => await Utils.GoToPageAsync(nameof(GenerateFilePage));
    
    private async Task NavigateToRunPathPageAsync(object? state)
        => await Utils.GoToPageAsync(nameof(RunPatchPage));

    private async Task ReloadAsync(object? state)
    {
        await LoadResourcePatchVersionsAsync(SelectedServerGroupType);
    }

    private async Task LoadResourcePatchVersionsAsync(ServerGroupType serverGroupType)
    {
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        try
        {
            using var conn = await _dbConnector.OpenAsync(ct);

            var versions = await ResourceDb.GetLatestResourcePatchVersions(
                conn, serverGroupType, 100, ct);
            
            ResourcePatchVersions.Clear();
            foreach (var entity in versions)
            {
                ResourcePatchVersions.Add(new ResourcePatchVersionModel(entity));
            }
        }
        catch (Exception e)
        {
            await Toast.Make($"An exception occurred: {e.Message}", ToastDuration.Long).Show(ct);
        }
    }
}
