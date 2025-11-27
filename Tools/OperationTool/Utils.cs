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
