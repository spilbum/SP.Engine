using System.Collections.ObjectModel;
using OperationTool.DatabaseHandler;
using OperationTool.Services;

namespace OperationTool.ViewModels;

public sealed class LocalizationFile(ResourceDb.LocalizationFileEntity e)
{
    public int FileId { get; } = e.FileId;
    public string? Comment { get; } = e.Comment;
    public string CreatedUtcText { get; } = e.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss");
}

public sealed class LocalizationActive(ResourceDb.LocalizationActiveEntity e)
{
    public string ServerGroupType { get; } = e.ServerGroupType;
    public string StoreType { get; } = e.StoreType;
    public int FileId { get; } = e.FileId;
    public string UpdatedUtcText { get; } = e.UpdatedUtc.ToString("yyyy-MM-dd HH:mm:ss");
}

public class LocalizationTabViewModel : ViewModelBase
{
    private readonly IDbConnector _db;
    private bool _isBusy;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            PatchCommand.RaiseCanExecuteChanged();
            GenerateCommand.RaiseCanExecuteChanged();
        }
    }

    public ObservableCollection<LocalizationActive> Actives { get; } = [];
    public ObservableCollection<LocalizationFile> LatestFiles { get; } = [];

    public AsyncRelayCommand PatchCommand { get; }
    public AsyncRelayCommand GenerateCommand { get; }

    public LocalizationTabViewModel(IDbConnector db)
    {
        _db = db;

        PatchCommand = new AsyncRelayCommand(
            PatchAsync,
            () => !IsBusy);

        GenerateCommand = new AsyncRelayCommand(
            GenerateAsync,
            () => !IsBusy);
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;

            using var conn = await _db.OpenAsync(ct);

            var actives = await ResourceDb.GetLocalizationActivesAsync(conn, ct);
            
            Actives.Clear();
            foreach (var entry in actives
                         .OrderBy(a => a.ServerGroupType, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(a => a.StoreType, StringComparer.OrdinalIgnoreCase))
            {
                Actives.Add(new LocalizationActive(entry));
            }

            var files = await ResourceDb.GetLocalizationFilesAsync(conn, ct);
            LatestFiles.Clear();
            foreach (var entry in files.OrderByDescending(f => f.FileId).Take(50))
            {
                LatestFiles.Add(new LocalizationFile(entry));
            }
        }
        catch (Exception e)
        {
            await Utils.AlertAsync(AlertLevel.Error, $"Load failed: {e.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static Task PatchAsync()
        => Shell.Current.GoToAsync(nameof(Pages.PatchLocalizationFilePage));

    private static Task GenerateAsync()
        => Shell.Current.GoToAsync(nameof(Pages.GenerateLocalizationFilePage));
}
