using System.Globalization;

namespace OperationTool;

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
