using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Storage;
using Microsoft.Extensions.Logging;
using OperationTool.Excel;
using OperationTool.Pages;
using OperationTool.Services;
using OperationTool.ViewModels;

namespace OperationTool;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts => { fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"); });

        builder.Services.AddSingleton<AppShell>();

        // Page
        builder.Services.AddTransient<PatchTabPage>();
        builder.Services.AddTransient<RunPatchPage>();
        builder.Services.AddTransient<GenerateFilePage>();
        builder.Services.AddTransient<VersionTabPage>();
        builder.Services.AddTransient<SettingsTabPage>();
        builder.Services.AddTransient<RefsDiffTabPage>();
        builder.Services.AddTransient<RefsSqlTabPage>();
        builder.Services.AddSingleton<LoadingPage>();
        builder.Services.AddSingleton<MaintenanceTabPage>();
        
        // ViewModel
        builder.Services.AddTransient<PatchTabViewModel>();
        builder.Services.AddTransient<RunPatchViewModel>();
        builder.Services.AddTransient<GenerateFileViewModel>();
        builder.Services.AddTransient<VersionTabViewModel>();
        builder.Services.AddTransient<SettingsTabViewModel>();
        builder.Services.AddTransient<RefsDiffTabViewModel>();
        builder.Services.AddTransient<RefsSqlTabViewModel>();
        builder.Services.AddSingleton<MaintenanceTabViewModel>();
        
        // Service
        builder.Services.AddSingleton<IExcelService, ExcelService>();
        builder.Services.AddSingleton<ISettingsStorage, FileSettingsStorage>();
        builder.Services.AddSingleton<ISettingsProvider, ToolSettingsProvider>();
        builder.Services.AddSingleton<IDbConnector, MySqlDbConnector>();
        builder.Services.AddSingleton<IResourceConfigStore, ResourceConfigStore>();
        builder.Services.AddSingleton(FolderPicker.Default);
        builder.Services.AddSingleton(FilePicker.Default);
        builder.Services.AddSingleton<IFileUploader, MinioUploader>();
        builder.Services.AddSingleton<HttpClient>();
        builder.Services.AddSingleton<ResourceServerWebService>();
        builder.Services.AddSingleton<ToolWarmupService>();
        builder.Services.AddSingleton<IDialogService, DialogService>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
