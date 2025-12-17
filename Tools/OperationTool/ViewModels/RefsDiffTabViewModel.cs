using System.Collections.ObjectModel;
using CommunityToolkit.Maui.Alerts;
using OperationTool.Diff;
using OperationTool.Diff.Models;
using OperationTool.Models;
using SP.Shared.Resource;

namespace OperationTool.ViewModels;

public sealed class RefsDiffTabViewModel : ViewModelBase
{
    private readonly IFilePicker _filePicker;
    private string _oldSchsPath = string.Empty;
    private string _oldRefsPath = string.Empty;
    private string _newSchsPath = string.Empty;
    private string _newRefsPath = string.Empty;
    private bool _isBusy;
    private TableDiffModel? _selectedTable;

    public string OldSchsPath
    {
        get => _oldSchsPath;
        set
        {
            if (SetProperty(ref _oldSchsPath, value))
                RunDiffCommand.RaiseCanExecuteChanged();
        }
    }

    public string OldRefsPath
    {
        get => _oldRefsPath;
        set
        {
            if (SetProperty(ref _oldRefsPath, value))
                RunDiffCommand.RaiseCanExecuteChanged();
        }
    }

    public string NewSchsPath
    {
        get => _newSchsPath;
        set
        {
            if (SetProperty(ref _newSchsPath, value))
                RunDiffCommand.RaiseCanExecuteChanged();
        }
    }

    public string NewRefsPath
    {
        get => _newRefsPath;
        set
        {
            if (SetProperty(ref _newRefsPath, value))
                RunDiffCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public TableDiffModel? SelectedTable
    {
        get => _selectedTable;
        set => SetProperty(ref _selectedTable, value);
    }

    public ObservableCollection<TableDiffModel> Tables { get; } = [];

    public AsyncRelayCommand BrowseOldSchsCommand { get; }
    public AsyncRelayCommand BrowseOldRefsCommand { get; }
    public AsyncRelayCommand BrowseNewSchsCommand { get; }
    public AsyncRelayCommand BrowseNewRefsCommand { get; }
    public AsyncRelayCommand RunDiffCommand { get; }


    public RefsDiffTabViewModel(IFilePicker filePicker)
    {
        _filePicker = filePicker;

        BrowseOldSchsCommand = new AsyncRelayCommand(BrowseOldSchsAsync);
        BrowseOldRefsCommand = new AsyncRelayCommand(BrowseOldRefsAsync);
        BrowseNewSchsCommand = new AsyncRelayCommand(BrowseNewSchsAsync);
        BrowseNewRefsCommand = new AsyncRelayCommand(BrowseNewRefsAsync);
        RunDiffCommand = new AsyncRelayCommand(RunDiffAsync, CanRunDiff);
    }
    
    private bool CanRunDiff()
    {
        return !string.IsNullOrWhiteSpace(OldSchsPath)
               && !string.IsNullOrWhiteSpace(OldRefsPath)
               && !string.IsNullOrWhiteSpace(NewSchsPath)
               && !string.IsNullOrWhiteSpace(NewRefsPath)
               && !IsBusy;
    }
    
    private async Task BrowseOldSchsAsync(object? state)
    {
        var result = await _filePicker.PickAsync();
        if (string.IsNullOrEmpty(result?.FileName))
            return;
        
        if (!Utils.ValidateExtension(result, "schs"))
        {
            await Toast.Make("Only SCHS files can be selected.").Show(CancellationToken.None);
            return;
        }

        OldSchsPath = result.FullPath;
    }

    private async Task BrowseOldRefsAsync(object? state)
    {
        var result = await _filePicker.PickAsync();
        if (string.IsNullOrEmpty(result?.FileName))
            return;
        
        if (!Utils.ValidateExtension(result, "refs"))
        {
            await Toast.Make("Only REFS files can be selected.").Show(CancellationToken.None);
            return;
        }

        OldRefsPath = result.FullPath;
    }

    private async Task BrowseNewSchsAsync(object? state)
    {
        var result = await _filePicker.PickAsync();
        if (string.IsNullOrEmpty(result?.FileName))
            return;
        
        if (!Utils.ValidateExtension(result, "schs"))
        {
            await Toast.Make("Only SCHS files can be selected.").Show(CancellationToken.None);
            return;
        }

        NewSchsPath = result.FullPath;
    }

    private async Task BrowseNewRefsAsync(object? state)
    {
        var result = await _filePicker.PickAsync();
        if (string.IsNullOrEmpty(result?.FileName))
            return;
        
        if (!Utils.ValidateExtension(result, "refs"))
        {
            await Toast.Make("Only REFS files can be selected.").Show(CancellationToken.None);
            return;
        }

        NewRefsPath = result.FullPath;
    }

    private async Task RunDiffAsync(object? state)
    {
        IsBusy = true;
        RunDiffCommand.RaiseCanExecuteChanged();

        var cts = new CancellationTokenSource();
        var ct = cts.Token;

        try
        {
            var oldSnap = await RefsSnapshotFactory.FromPackAsync(OldSchsPath, OldRefsPath, ct);
            var newSnap = await RefsSnapshotFactory.FromPackAsync(NewSchsPath, NewRefsPath, ct);

            var diff = RefsDiffService.Diff(oldSnap, newSnap);
            ApplyDiff(diff);
        }
        catch (Exception e)
        {
            await Toast.Make($"Refs diff failed: {e.Message}").Show(ct);
        }
        finally
        {
            IsBusy = false;
            RunDiffCommand.RaiseCanExecuteChanged();
        }
    }

    private void ApplyDiff(RefsDiffResult diff)
    {
        Tables.Clear();

        foreach (var table in diff.Tables.OrderBy(t => t.Name))
        {
            Tables.Add(new TableDiffModel(table));
        }
        
        SelectedTable = Tables.FirstOrDefault();
    }
}
