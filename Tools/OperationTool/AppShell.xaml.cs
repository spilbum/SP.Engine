using OperationTool.Pages;

namespace OperationTool;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        
        Routing.RegisterRoute(nameof(GenerateFilePage), typeof(GenerateFilePage));
        Routing.RegisterRoute(nameof(RunPatchPage), typeof(RunPatchPage));
    }
}

