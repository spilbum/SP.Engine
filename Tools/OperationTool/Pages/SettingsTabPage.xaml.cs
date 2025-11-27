using OperationTool.ViewModels;

namespace OperationTool.Pages;

public partial class SettingsTabPage : ContentPage
{
    public SettingsTabPage(SettingsTabViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}

