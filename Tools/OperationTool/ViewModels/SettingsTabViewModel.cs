using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Storage;
using OperationTool.DatabaseHandler;
using OperationTool.Models;
using OperationTool.Storage;

namespace OperationTool.ViewModels;

public sealed class SettingsTabViewModel : ViewModelBase
{
    private readonly ISettingsProvider _provider;
    private readonly IDbConnector _dbConnector;
    private bool _saveEnabled;

    public bool SaveEnabled
    {
        get => _saveEnabled;
        set
        {
            if (SetProperty(ref _saveEnabled, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
                TestConnectionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string _host = "127.0.0.1";
    private string _port = "3306";
    private string _database = "resource_db";
    private string _user = "root";
    private string _password = "";

    public string Host
    {
        get => _host;
        set
        {
            if (SetProperty(ref _host, value))
            {
                SaveEnabled = true;
            }
        }
    }

    public string Port
    {
        get => _port;
        set
        {
            if (SetProperty(ref _port, value))
            {
                SaveEnabled = true;
            }
        }
    }

    public string Database
    {
        get => _database;
        set
        {
            if (SetProperty(ref _database, value))
            {
                SaveEnabled = true;
            }
        }
    }

    public string User
    {
        get => _user;
        set
        {
            if (SetProperty(ref _user, value))
            {
                SaveEnabled = true;
            }
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value))
            {
                SaveEnabled = true;
            }
        }
    }
    
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand TestConnectionCommand { get; }
    
    public SettingsTabViewModel(
        ISettingsProvider provider,
        IDbConnector dbConnector)
    {
        _provider = provider;
        _dbConnector = dbConnector;
        
        var s = provider.Current.Database;
        _host = s.Host;
        _port = s.Port.ToString();
        _database = s.Database;
        _user = s.User;
        _password = s.Password;
        
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, CanTestConnection);
    }

    private async Task SaveAsync(object? parameter)
    {
        var s = _provider.Current;
        s.Database.Host = Host;
        s.Database.Port = int.Parse(Port);
        s.Database.Database = Database;
        s.Database.User = User;
        s.Database.Password = Password;
        await _provider.SaveAsync();
        await Toast.Make("Save settings successfully.").Show(CancellationToken.None);
        
        SaveEnabled = false;
    }

    private bool CanSave()
        => SaveEnabled;

    private async Task TestConnectionAsync(object? parameter)
    {
        var cts = new CancellationTokenSource();
        var ct = cts.Token;
        
        try
        {
            using var conn = await _dbConnector.OpenAsync(ct);
            await Toast.Make("Test connection successfully.").Show(ct);
        }
        catch (Exception e)
        {
            await Toast.Make($"An exception occurred: {e.Message}").Show(ct);
        }
    }

    private bool CanTestConnection()
        => _dbConnector.CanOpen() && !SaveEnabled;


}
