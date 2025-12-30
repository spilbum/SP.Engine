using System.Windows.Input;
using CommunityToolkit.Maui.Alerts;

namespace OperationTool;

public sealed class AsyncRelayCommand(
    Func<Task> execute,
    Func<bool>? canExecute = null)
    : AsyncRelayCommand<object>(
        _ => execute(),
        canExecute == null ? null : _ => canExecute());

public class AsyncRelayCommand<T>(
    Func<T?, Task> execute,
    Func<T?, bool>? canExecute = null) 
    : ICommand
{
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        if (_isExecuting)
            return false;
        
        if (canExecute == null)
            return true;

        return parameter switch
        {
            T t => canExecute!(t),
            null => canExecute(default),
            _ => false
        };
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        _isExecuting = true;
        RaiseCanExecuteChanged();

        try
        {
            var value = parameter is T t ? t : default;
            await execute(value);
        }
        catch (Exception ex)
        {
            await Toast.Make($"Failed to execute command: {ex.Message}").Show();
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
