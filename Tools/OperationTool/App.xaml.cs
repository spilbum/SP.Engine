using OperationTool.Pages;
using OperationTool.Services;
using OperationTool.ViewModels;

namespace OperationTool;

public partial class App : Application
{
    public App(IServiceProvider sp)
    {
        InitializeComponent();
        Current!.UserAppTheme = AppTheme.Light;

        var shell = sp.GetRequiredService<AppShell>();

        var vm = new LoadingViewModel(
            sp.GetRequiredService<ToolWarmupService>(),
            async () => { await MainThread.InvokeOnMainThreadAsync(() => MainPage = shell); });

        MainPage = new LoadingPage(vm);
    }
}
