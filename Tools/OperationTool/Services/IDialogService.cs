namespace OperationTool.Services;

public interface IDialogService
{
    Task AlertAsync(string title, string message, string ok = "OK");
}

public sealed class DialogService : IDialogService
{
    public Task AlertAsync(string title, string message, string ok = "OK")
        => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.DisplayAlert(title, message, ok));
}
