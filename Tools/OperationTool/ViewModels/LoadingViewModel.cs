using OperationTool.Services;

namespace OperationTool.ViewModels;

public sealed class LoadingViewModel : ViewModelBase
{
    private readonly ToolWarmupService _warmup;
    private readonly Func<Task> _onSuccessNavigate;

    private bool _isBusy;
    private string _title = "Initializing Operation Tool...";
    private string _statusMessage = "Loading configuration and preparing services";
    private string _errorMessage = "";

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }
    
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    
    public AsyncRelayCommand RetryCommand { get; }

    public LoadingViewModel(ToolWarmupService warmup, Func<Task> onSuccessNavigate)
    {
        _warmup = warmup;
        _onSuccessNavigate = onSuccessNavigate;

        RetryCommand = new AsyncRelayCommand(RunAsync);
    }

    public async Task StartAsync() => await RunAsync();

    private async Task RunAsync()
    {
        if (IsBusy) return;
        
        ErrorMessage = "";
        IsBusy = true;

        try
        {
            StatusMessage = "Warming up...";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _warmup.StartAsync(cts.Token);

            await Task.Delay(500, cts.Token);

            await _onSuccessNavigate();
        }
        catch (Exception e)
        {
            Title = "Initialization failed";
            StatusMessage = "Please retry.";
            ErrorMessage = e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
