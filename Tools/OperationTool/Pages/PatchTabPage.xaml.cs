using OperationTool.ViewModels;

namespace OperationTool.Pages;

public partial class PatchTabPage : ContentPage
{
    public PatchTabPage(PatchTabViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}

