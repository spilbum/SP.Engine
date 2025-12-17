using System.Collections.ObjectModel;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using OperationTool.DatabaseHandler;
using OperationTool.Models;
using OperationTool.Services;
using SP.Shared.Resource;
using SP.Shared.Resource.Web;

namespace OperationTool.ViewModels;

public sealed class VersionTabViewModel : ViewModelBase
{
    private readonly IDbConnector _dbConnector;
    private readonly ResourceServerWebService _webService;
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
    
    public List<StoreType> StoreTypes { get; } = [];
    public ObservableCollection<ClientBuildVersionModel> ClientBuildVersions { get; } = [];
    public AsyncRelayCommand<ClientBuildVersionModel> PromoteCommand { get; }
    public AsyncRelayCommand<ClientBuildVersionModel> UpdateCommand { get; }

    public VersionTabViewModel(IDbConnector dbConnector, ResourceServerWebService webService)
    {
        _dbConnector = dbConnector;
        _webService = webService;

        foreach (StoreType storeType in Enum.GetValues(typeof(StoreType)))
        {
            if (storeType == StoreType.None) continue;
            StoreTypes.Add(storeType);
        }
        SelectedStoreType = StoreTypes.FirstOrDefault();
        
        UpdateCommand = new AsyncRelayCommand<ClientBuildVersionModel>(UpdateAsync, CanUpdate);
        PromoteCommand = new AsyncRelayCommand<ClientBuildVersionModel>(PromoteAsync, CanPromote);
    }

    private void AttachModel(ClientBuildVersionModel m)
    {
        m.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ClientBuildVersionModel.BeginBuildVersion)
                or nameof(ClientBuildVersionModel.EndBuildVersion)
                or nameof(ClientBuildVersionModel.IsDirty)
                or nameof(ClientBuildVersionModel.IsValid))
            {
                UpdateCommand.RaiseCanExecuteChanged();
            }
        };
    }

    private bool CanUpdate(ClientBuildVersionModel? model)
    {
        if (model == null) return false;
        if (!model.IsUpdatable) return false;
        return model is { IsDirty: true, IsValid: true };
    }

    private async Task UpdateAsync(ClientBuildVersionModel? model)
    {
        if (model == null) return;

        var confirm = await Shell.Current.DisplayAlert(
            "Update",
            "Would you like to update the build version??",
            "Ok",
            "Cancel");
        
        if (!confirm)
            return;
        
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        try
        {
            using var conn = await _dbConnector.OpenAsync(ct);
            await conn.BeginTransactionAsync(ct);

            await ResourceDb.UpsertClientBuildVersionAsync(conn, (ResourceDb.ClientBuildVersionEntity)model, ct);
            
            model.AcceptChanges();
            UpdateCommand.RaiseCanExecuteChanged();

            await _webService.RefreshAsync(ct);
            
            await conn.CommitAsync(ct);
            await Toast.Make($"{model.ServerGroupType} updated").Show(ct);
        }
        catch (Exception e)
        {
            await Toast.Make($"Failed to update {model.ServerGroupType}: {e.Message}").Show(ct);
        }
    }

    private bool CanPromote(ClientBuildVersionModel? model)
        => model?.IsPromotable ?? false;

    private async Task PromoteAsync(ClientBuildVersionModel? src)
    {
        var confirm = await Shell.Current.DisplayAlert(
            "Promote",
            "Would you like to be promoted?",
            "Ok",
            "Cancel");
        
        if (!confirm)
            return;
        
        if (src == null)
            return;

        var dev = GetOrCreate(SelectedStoreType, ServerGroupType.Dev);
        var qa = GetOrCreate(SelectedStoreType, ServerGroupType.QA);
        var stage = GetOrCreate(SelectedStoreType, ServerGroupType.Stage);
        var live = GetOrCreate(SelectedStoreType, ServerGroupType.Live);

        if (!BuildVersion.TryParse(dev.EndBuildVersion, out var devVer))
            throw new InvalidOperationException("Dev version parse failed.");

        if (!BuildVersion.TryParse(qa.EndBuildVersion, out var qaVer))
            qaVer = default;
        
        if (!BuildVersion.TryParse(stage.EndBuildVersion, out var stageVer))
            stageVer = default;

        var nextDev = new BuildVersion(devVer.Major, devVer.Minor, devVer.Build + 1);

        switch (src.ServerGroupType)
        {
            case ServerGroupType.Dev:
            {
                SetBuildVersion(qa, devVer);
                SetBuildVersion(dev, nextDev);
                break;
            }
            case ServerGroupType.QA:
            {
                if (qaVer.Equals(default))
                    throw new InvalidOperationException("QA version is empty.");
                
                SetBuildVersion(stage, qaVer);
                SetBuildVersion(qa, devVer);
                SetBuildVersion(dev, nextDev);
                break;
            }
            case ServerGroupType.Stage:
            {
                if (qaVer.Equals(default))
                    throw new InvalidOperationException("QA version is empty.");
                
                if (stageVer.Equals(default))
                    throw new InvalidOperationException("Stage version is empty.");

                if (!string.IsNullOrWhiteSpace(live.BeginBuildVersion) &&
                    BuildVersion.TryParse(live.BeginBuildVersion, out var b) &&
                    b.CompareTo(stageVer) > 0)
                {
                    throw new InvalidOperationException("Live Begin is greater than new Live End.");
                }
                
                SetLiveEndOnly(live, stageVer);
                SetBuildVersion(stage, qaVer);
                SetBuildVersion(qa, devVer);
                SetBuildVersion(dev, nextDev);
                break;
            }
            case ServerGroupType.None:
            case ServerGroupType.Live:
            default:
                return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = cts.Token;
        
        try
        {
            using var conn = await _dbConnector.OpenAsync(ct);
            await conn.BeginTransactionAsync(ct);

            await ResourceDb.UpsertClientBuildVersionAsync(conn, (ResourceDb.ClientBuildVersionEntity)dev, ct);
            await ResourceDb.UpsertClientBuildVersionAsync(conn, (ResourceDb.ClientBuildVersionEntity)qa, ct);
            await ResourceDb.UpsertClientBuildVersionAsync(conn, (ResourceDb.ClientBuildVersionEntity)stage, ct);
            await ResourceDb.UpsertClientBuildVersionAsync(conn, (ResourceDb.ClientBuildVersionEntity)live, ct);

            await conn.CommitAsync(ct);
        }
        catch (Exception e)
        {
            await Toast.Make($"Failed to promote: {e.Message}").Show(ct);
        }
        
        UpdateCommand.RaiseCanExecuteChanged();
    }

    private ClientBuildVersionModel GetOrCreate(StoreType storeType, ServerGroupType serverGroupType)
    {
        var model = ClientBuildVersions.FirstOrDefault(x =>
            x.StoreType.Equals(storeType) && x.ServerGroupType == serverGroupType);

        if (model != null) return model;

        model = new ClientBuildVersionModel(
            storeType,
            serverGroupType,
            "0.0.0",
            "0.0.0"
        );
        
        ClientBuildVersions.Add(model);
        return model;
    }

    private static void SetBuildVersion(ClientBuildVersionModel model, BuildVersion v)
    {
        var str = v.ToString();
        model.BeginBuildVersion = str;
        model.EndBuildVersion = str;
    }

    private static void SetLiveEndOnly(ClientBuildVersionModel model, BuildVersion end)
    {
        model.EndBuildVersion = end.ToString();
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
            foreach (var m in 
                     from entity in entities 
                     where (StoreType)entity.StoreType == storeType 
                     select new ClientBuildVersionModel(entity))
            {
                AttachModel(m);
                ClientBuildVersions.Add(m);
            }
            
            UpdateCommand.RaiseCanExecuteChanged();
        }
        catch (Exception e)
        {
            await Toast.Make($"An exception occurred: {e.Message}", ToastDuration.Long).Show(ct);
        }
    }
}
