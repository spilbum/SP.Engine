using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OperationTool.ViewModels;

namespace OperationTool.Pages;

public partial class GenerateFilePage : ContentPage
{
    public GenerateFilePage(GenerateFileViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}

