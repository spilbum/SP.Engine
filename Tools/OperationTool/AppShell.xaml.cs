using OperationTool.Pages;

namespace OperationTool;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        
        Routing.RegisterRoute(nameof(GenerateRefsFilePage), typeof(GenerateRefsFilePage));
        Routing.RegisterRoute(nameof(PatchRefsFilePage), typeof(PatchRefsFilePage));
        Routing.RegisterRoute(nameof(RefsDiffTabPage), typeof(RefsDiffTabPage));
        
        Routing.RegisterRoute(nameof(GenerateLocalizationFilePage), typeof(GenerateLocalizationFilePage));
        Routing.RegisterRoute(nameof(PatchLocalizationFilePage), typeof(PatchLocalizationFilePage));
    }
}

