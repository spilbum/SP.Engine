using OperationTool.ViewModels;

namespace OperationTool.Pages;

public partial class PatchTabPage : ContentPage
{
    public PatchTabPage(PatchTabViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
    
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is PatchTabViewModel vm)
            await vm.LoadAsync();
    }
}

