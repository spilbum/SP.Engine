using OperationTool.ViewModels;

namespace OperationTool.Pages;

public partial class VersionTabPage : ContentPage
{
    public VersionTabPage(VersionTabViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is VersionTabViewModel vm)
            await vm.LoadAsync();
    }
}

