using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OperationTool.ViewModels;

namespace OperationTool.Pages;

public partial class PatchLocalizationFilePage : ContentPage
{
    public PatchLocalizationFilePage(PatchLocalizationFileViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is PatchLocalizationFileViewModel vm)
            await vm.LoadAsync();
    }
}

