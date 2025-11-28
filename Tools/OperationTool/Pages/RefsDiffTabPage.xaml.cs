using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OperationTool.ViewModels;

namespace OperationTool.Pages;

public partial class RefsDiffTabPage : ContentPage
{
    public RefsDiffTabPage(RefsDiffTabViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}

