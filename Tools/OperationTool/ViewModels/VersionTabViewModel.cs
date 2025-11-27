using System.Collections.ObjectModel;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using OperationTool.DatabaseHandler;
using OperationTool.Models;
using SP.Shared.Resource;

namespace OperationTool.ViewModels;

public sealed class VersionTabViewModel : ViewModelBase
{
    private readonly IDbConnector _dbConnector;
    private StoreType _selectedStoreType;

    public StoreType SelectedStoreType
    {
        get => _selectedStoreType;
        set
        {
            if (SetProperty(ref _selectedStoreType, value))
            {
                _ = LoadAsync(value);
            }
        }
    }
    
    public ObservableCollection<StoreType> StoreTypes { get; } = [];
    public ObservableCollection<ClientBuildVersionModel> ClientBuildVersions { get; } = [];

    public VersionTabViewModel(IDbConnector dbConnector)
    {
        _dbConnector = dbConnector;

        foreach (StoreType storeType in Enum.GetValues(typeof(StoreType)))
        {
            if (storeType == StoreType.Unknown)
                continue;
            
            StoreTypes.Add(storeType);
        }
        SelectedStoreType = StoreTypes.FirstOrDefault();
    }

    private async Task LoadAsync(StoreType storeType)
    {
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;
        
        try
        {
            using var conn = await _dbConnector.OpenAsync(ct);
            
            var entities = await ResourceDb.GetClientBuildVersions(conn, ct);
            
            ClientBuildVersions.Clear();
            foreach (var entity in entities)
            {
                if (!Enum.TryParse(entity.StoreType, out StoreType t) ||
                    t != storeType)
                    continue;
                ClientBuildVersions.Add(new ClientBuildVersionModel(entity));
            }
        }
        catch (Exception e)
        {
            await Toast.Make($"An exception occurred: {e.Message}", ToastDuration.Long).Show(ct);
        }
    }
}
