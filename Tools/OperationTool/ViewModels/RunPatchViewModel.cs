using System.Collections.ObjectModel;
using CommunityToolkit.Maui.Alerts;
using OperationTool.DatabaseHandler;
using OperationTool.Models;
using OperationTool.Services;
using SP.Shared.Resource;
using SP.Shared.Resource.Web;

namespace OperationTool.ViewModels;

public sealed class RunPatchViewModel : ViewModelBase
{
    private readonly IDialogService _dialog;
    private readonly IDbConnector _db;
    private readonly ResourceServerWebService _web;
    private RefsFileModel? _selectedRefsFile;
    private ServerGroupType _selectedServerGroupType;
    private int _resourceVersion;
    private int _selectedTargetMajor;
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

    public int SelectedTargetMajor
    {
        get => _selectedTargetMajor;
        set => SetProperty(ref _selectedTargetMajor, value);
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
                _ = LoadAsync(value);
        }
    }

    public ObservableCollection<ServerGroupType> ServerGroupTypes { get; } = [];
    public ObservableCollection<RefsFileModel> RefsFiles { get; } = [];
    public ObservableCollection<int> TargetMajors { get; } = [];
    public AsyncRelayCommand ExecuteCommand { get; }

    public RunPatchViewModel(
        IDialogService dialog, 
        IDbConnector db, 
        ResourceServerWebService web)
    {
        _dialog = dialog;
        _db = db;
        _web = web;

        foreach (ServerGroupType serverGroupType in Enum.GetValues(typeof(ServerGroupType)))
        {
            if (serverGroupType == ServerGroupType.None) continue;
            ServerGroupTypes.Add(serverGroupType);
        }
        SelectedServerGroupType = ServerGroupTypes.FirstOrDefault();

        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, CanExecute);
    }

    private async Task ExecuteAsync()
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
            using var conn = await _db.OpenAsync(ct);

            var entity = new ResourceDb.ResourcePatchVersionEntity
            {
                ServerGroupType = SelectedServerGroupType.ToString(),
                ResourceVersion = ResourceVersion,
                TargetMajor = SelectedTargetMajor,
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
            await _dialog.AlertAsync("Error", $"Failed to execute patch: {e.Message}");
        }
    }

    private async Task NotifyPatchAsync(CancellationToken ct)
    {
        try
        {
            await _web.RefreshAsync(ct);
            await Toast.Make("Patch notification sent").Show(ct);
        }
        catch (RpcException ex)
        {
            await _dialog.AlertAsync("Error", $"Notify failed: {ex.Message}");
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
            using var conn = await _db.OpenAsync(ct);
        
            var files = await ResourceDb.GetResourceRefsFilesAsync(conn, ct);
            
            RefsFiles.Clear();
            foreach (var entity in files.OrderByDescending(e => e.FileId))
            {
                RefsFiles.Add(new RefsFileModel(entity));
            }
            
            if (RefsFiles.Count == 0)
                throw new InvalidOperationException("No refs files found");
            
            SelectedRefsFile = RefsFiles.First();
        
            ResourceVersion = await ResourceDb.GetLatestResourceVersionAsync(conn, serverGroupType, ct) + 1;
        
            var versions = await ResourceDb.GetClientBuildVersions(conn, ct);
            var beginMajor = int.MaxValue;
            var endMajor = 0;
            
            TargetMajors.Clear();
            foreach (var entity in versions)
            {
                if (!Enum.TryParse(entity.ServerGroupType, out ServerGroupType t) || t != serverGroupType ||
                    !BuildVersion.TryParse(entity.BeginBuildVersion, out var begin) ||
                    !BuildVersion.TryParse(entity.EndBuildVersion, out var end))
                    continue;
            
                if (beginMajor > begin.Major)
                    beginMajor = begin.Major;
            
                if (endMajor < end.Major)
                    endMajor = end.Major;
            }
            
            if (beginMajor == endMajor)
                TargetMajors.Add(beginMajor);
            else
            {
                for (var v = endMajor; v > 0; v--)
                    TargetMajors.Add(v);   
            }

            if (TargetMajors.Count == 0)
                throw new InvalidOperationException("No client major versions are available.");

            SelectedTargetMajor = TargetMajors.FirstOrDefault();
            
            IsEnabled = true;
            ExecuteCommand.RaiseCanExecuteChanged();
            await Toast.Make("LoadAsync successfully").Show(ct);
        }
        catch (Exception e)
        {
            IsEnabled = false;
            await _dialog.AlertAsync("Error", $"Load failed: {e.Message}");
        }
    }
}
