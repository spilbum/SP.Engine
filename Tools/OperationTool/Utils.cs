using System.Globalization;
using SP.Shared.Resource;

namespace OperationTool;

public enum AlertLevel
{
    Info,
    Warning,
    Error
}

public static class Utils
{
    public static async Task OpenFolderAsync(string dir)
    {
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException(dir);
        
        var uri = new Uri($"file:///{dir}");
        await Launcher.Default.OpenAsync(uri);
    }
    
    public static Task GoToPageAsync(string pageName)
        => Shell.Current.GoToAsync(pageName);
    
    public static bool ValidateExtension(FileResult file, string extension)
    {
        return file.FileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
    }
    
    public static byte ToOrder(ServerGroupType g) => g switch
    {
        ServerGroupType.Dev => 10,
        ServerGroupType.QA => 20,
        ServerGroupType.Stage => 30,
        ServerGroupType.Live => 40,
        _ => 255
    };
    
    public static Task AlertAsync(AlertLevel level, string message)
        => MainThread.InvokeOnMainThreadAsync(
            () => Shell.Current.DisplayAlert(level.ToString().ToUpper(), message, "Ok"));
}

public sealed class NullToFalseConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value != null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public sealed class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrEmpty(s);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public enum MaintenanceStatusKind
{
    Normal,
    Scheduled,
    InProgress,
    Expired
}

public sealed class MaintenanceStatusToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not MaintenanceStatusKind status)
            return Colors.Gray;

        return status switch
        {
            MaintenanceStatusKind.Normal     => Colors.Gray,
            MaintenanceStatusKind.Scheduled  => Colors.Orange,
            MaintenanceStatusKind.InProgress => Colors.Red,
            MaintenanceStatusKind.Expired    => Colors.Goldenrod,
            _ => Colors.Gray
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


public sealed class BoolToOpacityConverter : IValueConverter
{
    public double TrueOpacity { get; set; } = 1.0;
    public double FalseOpacity { get; set; } = 0.4;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? TrueOpacity : FalseOpacity;

        return TrueOpacity;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;

        return true; // null / 잘못된 타입일 때 기본적으로 활성화
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

