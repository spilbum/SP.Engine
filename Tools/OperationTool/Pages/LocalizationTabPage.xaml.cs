using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OperationTool.ViewModels;

namespace OperationTool.Pages;

public partial class LocalizationTabPage : ContentPage
{
    public LocalizationTabPage(LocalizationTabViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
    
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is LocalizationTabViewModel vm)
            await vm.LoadAsync();
    }
}

