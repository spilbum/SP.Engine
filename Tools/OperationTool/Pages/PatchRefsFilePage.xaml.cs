using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OperationTool.ViewModels;
using SP.Shared.Resource;

namespace OperationTool.Pages;

public partial class PatchRefsFilePage : ContentPage
{
    public PatchRefsFilePage(PatchRefsFileViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is PatchRefsFileViewModel vm)
            await vm.LoadAsync();
    }
}

