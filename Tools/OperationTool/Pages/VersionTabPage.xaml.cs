using OperationTool.ViewModels;

namespace OperationTool.Pages;

public partial class VersionTabPage : ContentPage
{
    public VersionTabPage(VersionTabViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}

