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
        builder.Services.AddSingleton<LoadingPage>();
        builder.Services.AddSingleton<PatchTabPage>();
        builder.Services.AddTransient<PatchRefsFilePage>();
        builder.Services.AddTransient<GenerateRefsFilePage>();
        builder.Services.AddTransient<RefsDiffTabPage>();
        builder.Services.AddSingleton<VersionTabPage>();
        builder.Services.AddSingleton<SettingsTabPage>();
        builder.Services.AddSingleton<RefsSqlTabPage>();
        builder.Services.AddSingleton<MaintenanceTabPage>();
        builder.Services.AddTransient<GenerateLocalizationFilePage>();
        builder.Services.AddTransient<PatchLocalizationFilePage>();
        builder.Services.AddSingleton<LocalizationTabPage>();
        
        // ViewModel
        builder.Services.AddSingleton<PatchTabViewModel>();
        builder.Services.AddTransient<PatchRefsFileViewModel>();
        builder.Services.AddTransient<GenerateRefsFileViewModel>();
        builder.Services.AddTransient<RefsDiffTabViewModel>();
        builder.Services.AddSingleton<VersionTabViewModel>();
        builder.Services.AddSingleton<SettingsTabViewModel>();
        builder.Services.AddSingleton<RefsSqlTabViewModel>();
        builder.Services.AddSingleton<MaintenanceTabViewModel>();
        builder.Services.AddTransient<GenerateLocalizationFileViewModel>();
        builder.Services.AddTransient<PatchLocalizationFileViewModel>();
        builder.Services.AddSingleton<LocalizationTabViewModel>();
        
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
        builder.Services.AddSingleton<ILocalizationService, LocalizationService>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
