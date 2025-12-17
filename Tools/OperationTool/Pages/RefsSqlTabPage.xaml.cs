using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OperationTool.ViewModels;

namespace OperationTool.Pages;

public partial class RefsSqlTabPage : ContentPage
{
    public RefsSqlTabPage(RefsSqlTabViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}

