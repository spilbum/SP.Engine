using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OperationTool.ViewModels;

namespace OperationTool.Pages;

public partial class RunPatchPage : ContentPage
{
    public RunPatchPage(RunPatchViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}

