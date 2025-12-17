using CommunityToolkit.Maui.Alerts;
using OperationTool.Services;

namespace OperationTool.ViewModels;

public sealed class SettingsTabViewModel : ViewModelBase
{
    private readonly ISettingsProvider _settingsProvider;
    private readonly IDbConnector _dbConnector;
    private bool _saveEnabled;

    public bool SaveEnabled
    {
        get => _saveEnabled;
        set
        {
            if (SetProperty(ref _saveEnabled, value))
                SaveCommand.RaiseCanExecuteChanged();
        }
    }

    private string _host;
    private string _port;
    private string _database;
    private string _user;
    private string _password;
    
    public string Host
    {
        get => _host;
        set
        {
            if (SetProperty(ref _host, value))
                SaveEnabled = true;
        }
    }

    public string Port
    {
        get => _port;
        set
        {
            if (SetProperty(ref _port, value))
                SaveEnabled = true;
        }
    }

    public string Database
    {
        get => _database;
        set
        {
            if (SetProperty(ref _database, value))
                SaveEnabled = true;
        }
    }

    public string User
    {
        get => _user;
        set
        {
            if (SetProperty(ref _user, value))
                SaveEnabled = true;
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value))
                SaveEnabled = true;
        }
    }
    
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand CheckDatabaseCommand { get; }
    
    public SettingsTabViewModel(
        ISettingsProvider settingsProvider,
        IDbConnector dbConnector)
    {
        _settingsProvider = settingsProvider;
        _dbConnector = dbConnector;

        var s = settingsProvider.Settings;
        _host = s.Database.Host;
        _port = s.Database.Port.ToString();
        _database = s.Database.Database;
        _user = s.Database.User;
        _password = s.Database.Password;
        
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
        CheckDatabaseCommand = new AsyncRelayCommand(CheckDatabaseAsync);
    }


    private async Task SaveAsync(object? parameter)
    {
        var s = _settingsProvider.Settings;
        s.Database.Host = Host;
        s.Database.Port = int.Parse(Port);
        s.Database.Database = Database;
        s.Database.User = User;
        s.Database.Password = Password;
        
        var connStr = $"Server={Host};Port={Port};Database={Database};User Id={User};Password={Password}";
        _dbConnector.AddOrUpdate(connStr);
        
        await _settingsProvider.SaveAsync();
        await Toast.Make("Save settings successfully.").Show(CancellationToken.None);
        
        SaveEnabled = false;
    }

    private bool CanSave()
        => SaveEnabled;

    private async Task CheckDatabaseAsync(object? parameter)
    {
        var cts = new CancellationTokenSource();
        var ct = cts.Token;
        
        try
        {
            var connStr = $"Server={Host};Port={Port};Database={Database};User Id={User};Password={Password}";
            var connector = new MySqlDbConnector();
            connector.AddOrUpdate(connStr);
            using var conn = await connector.OpenAsync(ct);
            await Toast.Make("Database connection successful.").Show(ct);
        }
        catch (Exception e)
        {
            await Toast.Make($"Failed to connect to database: {e.Message}").Show(ct);
        }
    }
}
