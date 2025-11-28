namespace OperationTool;

public partial class App : Application
{
    public App(AppShell shell)
    {
        InitializeComponent();
        Current!.UserAppTheme = AppTheme.Light;
        MainPage = shell;
    }
}
