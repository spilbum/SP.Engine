using System.Collections.ObjectModel;
using System.Globalization;
using OperationTool.DatabaseHandler;
using OperationTool.Services;
using SP.Shared.Resource;

namespace OperationTool.ViewModels;

public class MaintenanceBypassModel : ViewModelBase
{
    private MaintenanceBypassKind _kind;
    private string _value = "";
    private int _id;

    public int Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public MaintenanceBypassKind Kind
    {
        get => _kind;
        set => SetProperty(ref _kind, value);
    }

    public MaintenanceBypassModel(ResourceDb.MaintenanceBypassEntity entity)
    {
        if (!Enum.TryParse(entity.Kind, out MaintenanceBypassKind kind))
            throw new ArgumentException($"Invalid kind: {entity.Kind}", nameof(entity));
        
        Id = entity.Id;
        Kind = kind;
        Value = entity.Value;
    }
}

public class MaintenanceTabViewModel : ViewModelBase
{
    private readonly IDialogService _dialog;
    private readonly IDbConnector _db;
    private readonly ResourceServerWebService _web;
    
    private ServerGroupType _selectedServerGroupType;
    private MaintenanceBypassKind _selectedBypassKind;
    private string _statusText = "";
    private bool _isEnabled;
    private string _startUtcText = "";
    private string _endUtcText = "";
    private string _messageId = "";
    private string _comment = "";
    private string _bypassValue = "";
    private MaintenanceStatusKind _statusKind;

    public MaintenanceStatusKind StatusKind
    {
        get => _statusKind;
        set => SetProperty(ref _statusKind, value);
    }

    public string BypassValue
    {
        get => _bypassValue;
        set => SetProperty(ref _bypassValue, value);
    }

    public string Comment
    {
        get => _comment;
        set => SetProperty(ref _comment, value);
    }

    public string MessageId
    {
        get => _messageId;
        set => SetProperty(ref _messageId, value);
    }

    public string EndUtcText
    {
        get => _endUtcText;
        set => SetProperty(ref _endUtcText, value);
    }

    public string StartUtcText
    {
        get => _startUtcText;
        set => SetProperty(ref _startUtcText, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public ServerGroupType SelectedServerGroupType
    {
        get => _selectedServerGroupType;
        set
        {
            if (SetProperty(ref _selectedServerGroupType, value))
                _ = LoadAsync();
        }
    }

    public MaintenanceBypassKind SelectedBypassKind
    {
        get => _selectedBypassKind;
        set => SetProperty(ref _selectedBypassKind, value);
    }

    public ObservableCollection<ServerGroupType> ServerGroupTypes { get; } = [];
    public ObservableCollection<MaintenanceBypassKind> BypassKinds { get; } = [];
    public ObservableCollection<MaintenanceBypassModel> Bypasses { get; } = [];
    public AsyncRelayCommand ApplyEnvCommand { get; }
    public AsyncRelayCommand AddBypassCommand { get; }
    public AsyncRelayCommand<MaintenanceBypassModel> RemoveBypassCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    public MaintenanceTabViewModel(
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

        foreach (MaintenanceBypassKind bypassKind in Enum.GetValues(typeof(MaintenanceBypassKind)))
        {
            if (bypassKind == MaintenanceBypassKind.None) continue;
            BypassKinds.Add(bypassKind);
        }
        
        SelectedBypassKind = BypassKinds.FirstOrDefault();
        SelectedServerGroupType = ServerGroupTypes.FirstOrDefault();
        
        ApplyEnvCommand = new AsyncRelayCommand(ApplyEnv);
        AddBypassCommand = new AsyncRelayCommand(AddBypassAsync);
        RemoveBypassCommand = new AsyncRelayCommand<MaintenanceBypassModel>(RemoveAsync);
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
    }
    
    private static (MaintenanceStatusKind kind, string text) BuildStatus(ResourceDb.MaintenanceEnvEntity? env)
    {
        if (env is not { IsEnabled: true })
            return (MaintenanceStatusKind.Normal, "● Normal operation");

        var now = DateTime.UtcNow;
        var start = env.StartUtc;
        var end = env.EndUtc;

        if (now < start)
            return (MaintenanceStatusKind.Scheduled, $"● Scheduled ({start:yyyy-MM-dd HH:mm} ~ {end:yyyy-MM-dd HH:mm} UTC)");

        return now <= end 
            ? (MaintenanceStatusKind.InProgress, $"● In Maintenance ({start:yyyy-MM-dd HH:mm} ~ {end:yyyy-MM-dd HH:mm} UTC)") 
            : (MaintenanceStatusKind.Expired, $"● Expired (still enabled) ({start:yyyy-MM-dd HH:mm} ~ {end:yyyy-MM-dd HH:mm} UTC)");
    }
    
    private static DateTime ParseUtc(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            throw new FormatException("Start/End UTC datetime is required.");

        if (!DateTime.TryParseExact(
                text,
                ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.fff"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dt))
            throw new FormatException("UTC datetime format: yyyy-MM-dd HH:mm:ss (or .fff)");

        return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    }

    private async Task AddBypassAsync()
    {
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;
        
        try
        {
            var entity = new ResourceDb.MaintenanceBypassEntity
            {
                ServerGroupType = SelectedServerGroupType.ToString(),
                Kind = SelectedBypassKind.ToString(),
                Value = BypassValue,
            };
            
            using var conn = await _db.OpenAsync(ct);
            await ResourceDb.InsertMaintenanceBypassAsync(conn, entity, ct);

            BypassValue = "";
            
            await _web.RefreshAsync(ct);
            
            await LoadAsync();
        }
        catch (Exception e)
        {
            await _dialog.AlertAsync("Error", $"Failed to add bypass: {e.Message}");
        }
    }

    private async Task RemoveAsync(MaintenanceBypassModel? model)
    {
        if (model == null) return;
        
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;
        
        try
        {
            using var conn = await _db.OpenAsync(ct);
            await ResourceDb.RemoveMaintenanceBypassAsync(conn, model.Id, ct);

            await _web.RefreshAsync(ct);
            
            Bypasses.Remove(model);
        }
        catch (Exception e)
        {
            await _dialog.AlertAsync("Error", $"Failed to remove bypass: {e.Message}");
        }
    }

    private async Task ApplyEnv()
    {
        var ok = await Shell.Current.DisplayAlert(
            "",
            "Would you like to apply the check?",
            "Ok",
            "Cancel");
        
        if (!ok) return;
        
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;
        
        try
        {
            var start = ParseUtc(StartUtcText);
            var end   = ParseUtc(EndUtcText);
            if (end <= start)
                throw new FormatException("End must be greater than Start.");
            
            using var conn = await _db.OpenAsync(ct);
            await ResourceDb.UpsertMaintenanceEnvAsync(
                conn,
                new ResourceDb.MaintenanceEnvEntity
                {
                    ServerGroupType = SelectedServerGroupType.ToString(),
                    IsEnabled = IsEnabled,
                    StartUtc = start,
                    EndUtc = end,
                    MessageId = MessageId,
                    Comment = Comment,
                    UpdatedBy = Environment.UserName,
                }, ct);

            await _web.RefreshAsync(ct);
            await LoadAsync();
        }
        catch (Exception e)
        {
            await _dialog.AlertAsync("Error", $"Failed to apply maintenance env: {e.Message}");
        }
    }

    private async Task LoadAsync()
    {
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        try
        {
            using var conn = await _db.OpenAsync(ct);

            var env = await ResourceDb.GetMaintenanceEnvAsync(conn, SelectedServerGroupType, ct);
            if (env != null)
            {
                IsEnabled = env.IsEnabled;
                StartUtcText = env.StartUtc.ToString("yyyy-MM-dd HH:mm:ss");
                EndUtcText = env.EndUtc.ToString("yyyy-MM-dd HH:mm:ss");
                MessageId = env.MessageId;
                Comment = env.Comment ?? string.Empty;
            }
            else
            {
                IsEnabled = false;
                StartUtcText = "";
                EndUtcText = "";
                MessageId = "";
                Comment = "";
            }

            var bypasses = await ResourceDb.GetMaintenanceBypassAsync(conn, SelectedServerGroupType, ct);

            Bypasses.Clear();
            foreach (var e in bypasses)
                Bypasses.Add(new MaintenanceBypassModel(e));

            var (kind, text) = BuildStatus(env);
            StatusKind = kind;
            StatusText = text;
        }
        catch (Exception e)
        {
            await _dialog.AlertAsync("Error", $"Failed to load maintenance: {e.Message}");
        }
    }
}
