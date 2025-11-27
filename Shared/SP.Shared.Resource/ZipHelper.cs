using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace SP.Shared.Resource;

public static class ZipHelper
{
    public sealed class Entry
    {
        public string Name { get; set; } = string.Empty;
        public byte[] Data { get; set; } = [];
    }

    public static void PackDir(string dir, string searchPattern, string zipPath)
    {
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException(dir);
        
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        using var stream = File.Create(zipPath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        var files = Directory.GetFiles(dir, searchPattern, SearchOption.TopDirectoryOnly);

        foreach (var path in files)
        {
            var entryName = Path.GetFileName(path);
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            
            using var es = entry.Open();
            using var fs = File.OpenRead(path);
            fs.CopyTo(es);
        }
    }

    public static async Task PackDirAsync(
        string sourceDir,
        string searchPattern,
        string zipPath,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException(sourceDir);
        
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        await using var stream = new FileStream(
            zipPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            useAsync: true);
        
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        var files = Directory.GetFiles(sourceDir, searchPattern, SearchOption.TopDirectoryOnly);

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
            
            var entryName = Path.GetFileName(path);
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            
            await using var es = entry.Open();
            await using var fs = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                useAsync: true);
            
            await fs.CopyToAsync(es, ct).ConfigureAwait(false);
        }
    }

    public static List<Entry> ReadAll(string zipPath)
    {
        using var fs = File.OpenRead(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        
        var list = new List<Entry>();

        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;
            
            using var es = entry.Open();
            using var ms = new MemoryStream();
            es.CopyTo(ms);
            
            list.Add(new Entry { Name = entry.Name, Data = ms.ToArray() });
        }
        
        return list;
    }

    public static async Task<List<Entry>> ReadAllAsync(
        string zipPath,
        CancellationToken ct = default)
    {
        await using var fs = new FileStream(
            zipPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            useAsync: true);
        
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        
        var list = new List<Entry>();

        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;
            
            await using var es = entry.Open();
            await using var ms = new MemoryStream();
            await es.CopyToAsync(ms, ct).ConfigureAwait(false);
            
            list.Add(new Entry { Name = entry.Name, Data = ms.ToArray() });
        }
        return list;
    }
}
