using System.Collections.ObjectModel;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using OperationTool.DatabaseHandler;
using OperationTool.Models;
using OperationTool.Services;
using SP.Shared.Resource;

namespace OperationTool.ViewModels;

public sealed class VersionTabViewModel : ViewModelBase
{
    private readonly IDialogService _dialog;
    private readonly IDbConnector _db;
    private readonly ResourceServerWebService _web;
    private StoreType _selectedStoreType;

    public StoreType SelectedStoreType
    {
        get => _selectedStoreType;
        set
        {
            if (SetProperty(ref _selectedStoreType, value))
                _ = LoadAsync(value);
        }
    }
    
    public List<StoreType> StoreTypes { get; } = [];
    public ObservableCollection<ClientBuildVersionModel> ClientBuildVersions { get; } = [];
    public AsyncRelayCommand InitDefaultsCommand { get; }
    public AsyncRelayCommand<ClientBuildVersionModel> PrimaryActionCommand { get; }

    public VersionTabViewModel(IDialogService dialog, IDbConnector db, ResourceServerWebService web)
    {
        _dialog = dialog;
        _db = db;
        _web = web;

        foreach (StoreType storeType in Enum.GetValues(typeof(StoreType)))
        {
            if (storeType == StoreType.None) continue;
            StoreTypes.Add(storeType);
        }
        SelectedStoreType = StoreTypes.FirstOrDefault();
        
        InitDefaultsCommand = new AsyncRelayCommand(InitDefaultsAsync, CanInitDefaults);
        PrimaryActionCommand = new AsyncRelayCommand<ClientBuildVersionModel>(PrimaryActionAsync, CanPrimaryAction);
    }

    private bool CanPrimaryAction(ClientBuildVersionModel? m)
        => m is { IsValid: true } && (m.CanSave || m.CanPromote);

    private async Task PrimaryActionAsync(ClientBuildVersionModel? m)
    {
        if (m == null) return;

        if (m.CanSave)
            await UpdateAsync(m);
        else if (m.CanPromote)
            await PromoteAsync(m);
    }

    private void AttachModel(ClientBuildVersionModel m)
    {
        m.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(ClientBuildVersionModel.BeginBuildVersion)
                or nameof(ClientBuildVersionModel.EndBuildVersion)
                or nameof(ClientBuildVersionModel.IsDirty)
                or nameof(ClientBuildVersionModel.IsValid)))
            {
                return;
            }
            
            PrimaryActionCommand.RaiseCanExecuteChanged();
        };
    }
    
    private bool CanInitDefaults()
        => SelectedStoreType != StoreType.None && ClientBuildVersions.Count == 0;

    private async Task InitDefaultsAsync()
    {
        if (SelectedStoreType == StoreType.None)
            return;

        var ok = await Shell.Current.DisplayAlert(
            "Initialize",
            $"Create default Dev/QA/Stage/Live rows for [{SelectedStoreType}]?",
            "Ok",
            "Cancel");

        if (!ok) return;
        
        var dev   = new BuildVersion(1, 0, 4);
        var qa    = new BuildVersion(1, 0, 3);
        var stage = new BuildVersion(1, 0, 2);
        var live  = new BuildVersion(1, 0, 1);

        var rows = new[]
        {
            Make(ServerGroupType.Dev, SelectedStoreType, dev, dev),
            Make(ServerGroupType.QA, SelectedStoreType, qa, qa),
            Make(ServerGroupType.Stage, SelectedStoreType, stage, stage),
            Make(ServerGroupType.Live, SelectedStoreType, live, live),
        };
        
        try
        {

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            var ct = cts.Token;
            
            using var conn = await _db.OpenAsync(ct);
            await conn.BeginTransactionAsync(ct);

            foreach (var entity in rows)
                await ResourceDb.UpsertClientBuildVersionAsync(conn, entity, ct);
            
            await conn.CommitAsync(ct);

            await _web.RefreshAsync(ct);

            await LoadAsync(SelectedStoreType);
            await Toast.Make("Initialized").Show(ct);
        }
        catch (Exception e)
        {
            await _dialog.AlertAsync("Error", $"Failed to initialize build version: {e.Message}");
        }

        return;

        ResourceDb.ClientBuildVersionEntity Make(
            ServerGroupType serverGroupType, StoreType storeType, BuildVersion begin, BuildVersion end)
            => new()
            {
                ServerGroupType = serverGroupType.ToString(),
                StoreType = storeType.ToString(),
                BeginBuildVersion = begin.ToString(),
                EndBuildVersion = end.ToString(),
                ServerGroupOrder = Utils.ToOrder(serverGroupType)
            };
    }

    private async Task UpdateAsync(ClientBuildVersionModel? model)
    {
        if (model == null) return;

        var ok = await Shell.Current.DisplayAlert(
            "Update",
            "Would you like to update the build version??",
            "Ok",
            "Cancel");
        
        if (!ok)
            return;
        
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        try
        {
            using var conn = await _db.OpenAsync(ct);
            await ResourceDb.UpsertClientBuildVersionAsync(conn, (ResourceDb.ClientBuildVersionEntity)model, ct);
            
            model.MarkSaved();
            PrimaryActionCommand.RaiseCanExecuteChanged();
  
            await _web.RefreshAsync(ct);
            await Toast.Make($"{model.ServerGroupType} updated").Show(ct);
        }
        catch (Exception e)
        {
            await _dialog.AlertAsync(
                "Error", 
                $"Failed to update build version: {e.Message}");
        }
    }

    private async Task PromoteAsync(ClientBuildVersionModel? src)
    {
        if (src == null) return;

        var dev = GetOrCreate(SelectedStoreType, ServerGroupType.Dev);
        var qa = GetOrCreate(SelectedStoreType, ServerGroupType.QA);
        var stage = GetOrCreate(SelectedStoreType, ServerGroupType.Stage);
        var live = GetOrCreate(SelectedStoreType, ServerGroupType.Live);

        if (src.ServerGroupType is ServerGroupType.Dev or ServerGroupType.QA)
        {
            int MajorOf(ClientBuildVersionModel m)
                => BuildVersion.TryParse(m.EndBuildVersion, out var v) ? v.Major : -1;

            var devMajor = MajorOf(dev);
            var qaMajor = MajorOf(qa);
            var stageMajor = MajorOf(stage);

            var allowed = src.ServerGroupType switch
            {
                ServerGroupType.Dev => devMajor > qaMajor,
                ServerGroupType.QA => qaMajor > stageMajor,
                _ => true
            };

            if (!allowed)
            {
                await Toast.Make("Dev/QA promote is only for major version bump.", ToastDuration.Long).Show();
                return;
            }
        }

        if (!BuildVersion.TryParse(dev.EndBuildVersion, out var dev0))
        {
            await Toast.Make("Dev version is invalid").Show();
            return;
        }

        if (!BuildVersion.TryParse(qa.EndBuildVersion, out var qa0)) qa0 = default;
        if (!BuildVersion.TryParse(stage.EndBuildVersion, out var stage0)) stage0 = default;

        if (!BuildVersion.TryParse(live.BeginBuildVersion, out var liveBegin)) liveBegin = default;
        if (!BuildVersion.TryParse(live.EndBuildVersion, out var liveEnd)) liveEnd = default;
        
        var nextDev = new BuildVersion(dev0.Major, dev0.Minor, dev0.Build + 1);

        (BuildVersion Begin, BuildVersion End) newDev;
        (BuildVersion Begin, BuildVersion End) newQa;
        (BuildVersion Begin, BuildVersion End) newStage;
        (BuildVersion Begin, BuildVersion End) newLive;
        
        switch (src.ServerGroupType)
        {
            case ServerGroupType.Dev:
                newLive = (liveBegin, liveEnd);
                newStage = (stage0, stage0);
                newQa = (dev0, dev0);
                newDev = (nextDev, nextDev);
                break;
            
            case ServerGroupType.QA:
                if (qa0.Equals(default))
                {
                    await Toast.Make("QA version is empty").Show();
                    return;
                }

                newLive = (liveBegin, liveEnd);
                newStage = (qa0, qa0);
                newQa = (dev0, dev0);
                newDev = (nextDev, nextDev);
                break;
            
            case ServerGroupType.Stage:
                if (qa0.Equals(default))
                {
                    await Toast.Make("QA version is empty").Show();
                    return;
                }
                
                if (stage0.Equals(default))
                {
                    await Toast.Make("Stage version is empty").Show();
                    return;
                }

                if (!string.IsNullOrWhiteSpace(live.BeginBuildVersion) &&
                    BuildVersion.TryParse(live.BeginBuildVersion, out var b) &&
                    b.CompareTo(stage0) > 0)
                {
                    await Toast.Make("Live Begin is greater than new Live End").Show();
                    return;
                }
                
                if (!liveEnd.Equals(default) && stage0.CompareTo(liveEnd) < 0)
                {
                    await Toast.Make("New Live End is lower than current Live End").Show();
                    return;
                }
                
                newLive = (liveBegin, stage0);
                newStage = (qa0, qa0);
                newQa = (dev0, dev0);
                newDev = (nextDev, nextDev);
                break;
            
            case ServerGroupType.None:
            case ServerGroupType.Live:
            default:
                return;
        }
        
        var preview =
            $"Dev   : {dev.EndBuildVersion} -> {newDev.End}\n" +
            $"QA    : {qa.EndBuildVersion} -> {newQa.End}\n" +
            $"Stage : {stage.EndBuildVersion} -> {newStage.End}\n" +
            $"Live  : {live.BeginBuildVersion}~{live.EndBuildVersion} -> {newLive.Begin}~{newLive.End}";

        var ok = await Shell.Current.DisplayAlert("Promote", preview, "Apply", "Cancel");
        if (!ok) return;

        SetBuildVersion(dev, newDev.End);
        SetBuildVersion(qa, newQa.End);
        SetBuildVersion(stage, newStage.End);
        live.BeginBuildVersion = newLive.Begin.ToString();
        live.EndBuildVersion = newLive.End.ToString();

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = cts.Token;
        
        try
        {
            using var conn = await _db.OpenAsync(ct);
            await conn.BeginTransactionAsync(ct);

            await ResourceDb.UpsertClientBuildVersionAsync(conn, (ResourceDb.ClientBuildVersionEntity)dev, ct);
            await ResourceDb.UpsertClientBuildVersionAsync(conn, (ResourceDb.ClientBuildVersionEntity)qa, ct);
            await ResourceDb.UpsertClientBuildVersionAsync(conn, (ResourceDb.ClientBuildVersionEntity)stage, ct);
            await ResourceDb.UpsertClientBuildVersionAsync(conn, (ResourceDb.ClientBuildVersionEntity)live, ct);
            
            await conn.CommitAsync(ct);
            
            await _web.RefreshAsync(ct);
            
            dev.MarkSaved();
            qa.MarkSaved();
            stage.MarkSaved();
            live.MarkSaved();
            
            PrimaryActionCommand.RaiseCanExecuteChanged();
            await Toast.Make($"{src.ServerGroupType} promoted").Show(ct);
        }
        catch (Exception e)
        {
            await _dialog.AlertAsync("Error", $"Failed to promote build version: {e.Message}");
        }
    }

    private ClientBuildVersionModel GetOrCreate(
        StoreType storeType,
        ServerGroupType serverGroupType)
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
        AttachModel(model);
        return model;
    }

    private static void SetBuildVersion(ClientBuildVersionModel model, BuildVersion v)
    {
        var str = v.ToString();
        model.BeginBuildVersion = str;
        model.EndBuildVersion = str;
    }
    
    private async Task LoadAsync(StoreType storeType)
    {
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;
        
        try
        {
            using var conn = await _db.OpenAsync(ct);
            var entities = await ResourceDb.GetClientBuildVersions(conn, ct);
            
            ClientBuildVersions.Clear();
            foreach (var m in 
                     from entity in entities 
                     where entity.StoreType == storeType.ToString() 
                     select new ClientBuildVersionModel(entity))
            {
                AttachModel(m);
                ClientBuildVersions.Add(m);
            }
            
            InitDefaultsCommand.RaiseCanExecuteChanged();
            PrimaryActionCommand.RaiseCanExecuteChanged();
        }
        catch (Exception e)
        {
            await _dialog.AlertAsync("Error", $"Failed to load build version: {e.Message}");
        }
    }
}
