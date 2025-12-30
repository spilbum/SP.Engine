using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OperationTool.ViewModels;

namespace OperationTool.Pages;

public partial class MaintenanceTabPage : ContentPage
{
    public MaintenanceTabPage(MaintenanceTabViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}

