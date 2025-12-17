using System.Collections.ObjectModel;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using OperationTool.DatabaseHandler;
using OperationTool.Models;
using OperationTool.Services;
using SP.Shared.Resource;
using SP.Shared.Resource.Web;

namespace OperationTool.ViewModels;

public sealed class RunPatchViewModel : ViewModelBase
{
    private readonly IDbConnector _dbConnector;
    private readonly ResourceServerWebService _webService;
    private ServerGroupType _selectedServerGroupType;
    private RefsFileModel? _selectedRefsFile;
    private int _resourceVersion;
    private int _selectedClientMajorVersion;
    private bool _isEnabled;

    public bool IsEnabled
    {
        get => _isEnabled;
        private set
        {
            if (SetProperty(ref _isEnabled, value))
                ExecuteCommand.RaiseCanExecuteChanged();
        }
    }

    public int SelectedClientMajorVersion
    {
        get => _selectedClientMajorVersion;
        set => SetProperty(ref _selectedClientMajorVersion, value);
    }

    public int ResourceVersion
    {
        get => _resourceVersion;
        private set => SetProperty(ref _resourceVersion, value);
    }

    public RefsFileModel? SelectedRefsFile
    {
        get => _selectedRefsFile;
        set => SetProperty(ref _selectedRefsFile, value);
    }
    
    public ServerGroupType SelectedServerGroupType
    {
        get => _selectedServerGroupType;
        set
        {
            if (SetProperty(ref _selectedServerGroupType, value))
            {
                _ = LoadAsync(value);
            }
        }
    }

    public ObservableCollection<ServerGroupType> ServerGroupTypes { get; } = [];
    public ObservableCollection<RefsFileModel> RefsFiles { get; } = [];
    public ObservableCollection<int> ClientMajorVersions { get; } = [];
    
    public AsyncRelayCommand ExecuteCommand { get; }

    public RunPatchViewModel(IDbConnector dbConnector, ResourceServerWebService webService)
    {
        _dbConnector = dbConnector;
        _webService = webService;
        
        foreach (ServerGroupType serverGroupType in Enum.GetValues(typeof(ServerGroupType)))
            ServerGroupTypes.Add(serverGroupType);
        SelectedServerGroupType = ServerGroupTypes.FirstOrDefault();
        
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, CanExecute);
    }

    private async Task ExecuteAsync(object? state)
    {
        var confirm = await Shell.Current.DisplayAlert(
            "Patch",
            "Would you like to run the patch?",
            "Ok",
            "Cancel");
        
        if (!confirm)
            return;
        
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        try
        {
            using var conn = await _dbConnector.OpenAsync(ct);

            var entity = new ResourceDb.ResourcePatchVersionEntity
            {
                ServerGroupType = SelectedServerGroupType.ToString(),
                ResourceVersion = ResourceVersion,
                ClientMajorVersion = SelectedClientMajorVersion,
                FileId = SelectedRefsFile!.FileId,
                Comment = SelectedRefsFile!.Comment,
                CreatedUtc = DateTime.UtcNow
            };
            
            await ResourceDb.InsertResourcePatchVersionAsync(conn, entity, ct);
            await NotifyPatchAsync(ct);
            await Utils.GoToPageAsync("..");
            await Toast.Make("Execute patch successfully").Show(ct);
        }
        catch (Exception e)
        {
            await Toast.Make($"An exception occurred: {e.Message}", ToastDuration.Long).Show(ct);
        }
    }

    private async Task NotifyPatchAsync(CancellationToken ct)
    {
        try
        {
            await _webService.RefreshAsync(ct);
            await Toast.Make("Patch notification sent").Show(ct);
        }
        catch (RpcException ex)
        {
            await Toast.Make($"Notify failed: {ex.Message}").Show(ct);
        }
    }

    private bool CanExecute()
        => IsEnabled;

    private async Task LoadAsync(ServerGroupType serverGroupType)
    {
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        try
        {
            using var conn = await _dbConnector.OpenAsync(ct);
        
            var files = await ResourceDb.GetResourceRefsFiles(conn, ct);
            
            RefsFiles.Clear();
            foreach (var entity in files)
            {
                RefsFiles.Add(new RefsFileModel(entity));
            }
            
            if (RefsFiles.Count == 0)
                throw new InvalidOperationException("No refs files found");
            
            SelectedRefsFile = RefsFiles.LastOrDefault();
        
            ResourceVersion = await ResourceDb.GetLatestResourceVersionAsync(conn, serverGroupType, ct) + 1;
        
            var versions = await ResourceDb.GetClientBuildVersions(conn, ct);
            var beginVersion = int.MaxValue;
            var endVersion = 0;
            
            ClientMajorVersions.Clear();
            foreach (var entity in versions)
            {
                if ((ServerGroupType)entity.ServerGroupType != serverGroupType ||
                    !BuildVersion.TryParse(entity.BeginBuildVersion, out var begin) ||
                    !BuildVersion.TryParse(entity.EndBuildVersion, out var end))
                    continue;
            
                if (beginVersion > begin.Major)
                    beginVersion = begin.Major;
            
                if (endVersion < end.Major)
                    endVersion = end.Major;
            }
            
            if (beginVersion == endVersion)
                ClientMajorVersions.Add(beginVersion);
            else
            {
                for (var v = endVersion; v > 0; v--)
                    ClientMajorVersions.Add(v);   
            }

            if (ClientMajorVersions.Count == 0)
                throw new InvalidOperationException("No client major versions are available.");

            SelectedClientMajorVersion = ClientMajorVersions.FirstOrDefault();
            
            IsEnabled = true;
            ExecuteCommand.RaiseCanExecuteChanged();
            await Toast.Make("LoadAsync successfully").Show(ct);
        }
        catch (Exception e)
        {
            IsEnabled = false;
            await Toast.Make($"An exception occured: {e.Message}").Show(ct);
        }
    }
}
