using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OperationTool.ViewModels;

namespace OperationTool.Pages;

public partial class LoadingPage : ContentPage
{
    private readonly LoadingViewModel _vm;
    public LoadingPage(LoadingViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.StartAsync();
    }
}

