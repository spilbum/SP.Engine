namespace OperationTool;

public partial class App : Application
{
    public App(AppShell shell)
    {
        InitializeComponent();
        Current!.UserAppTheme = AppTheme.Light;
        MainPage = shell;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);

#if MACCALALYST
        window.Width = 1200;
        window.Height = 800;
#endif

        return window;
    }
}
