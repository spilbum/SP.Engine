using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SP.Resource.Localization;

public static class LocPackReader
{
    public static List<LocFile> Read(string locsFilePath)
    {
        var entries = ZipHelper.ReadAll(locsFilePath);
        var list = new List<LocFile>(entries.Count);
        list.AddRange(
            from entry in entries 
            where entry.Name.EndsWith($".{PatchConst.LocFile}", StringComparison.OrdinalIgnoreCase) 
            select LocFileReader.Read(entry.Data));

        return list;
    }
    
    public static async Task<List<LocFile>> ReadAsync(
        string locsFilePath,
        CancellationToken ct = default)
    {
        var entries = await ZipHelper.ReadAllAsync(locsFilePath, ct);
        var list = new List<LocFile>(entries.Count);

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            if (!entry.Name.EndsWith($".{PatchConst.LocFile}", StringComparison.OrdinalIgnoreCase))
                continue;

            var locFile = LocFileReader.Read(entry.Data);
            list.Add(locFile);
        }

        return list;
    }

    public static LocFile? Read(string locsFilePath, string language)
    {
        var entryName = GetEntryName(language);
        var data = ZipHelper.ReadEntry(locsFilePath, entryName);
        return data != null ? LocFileReader.Read(data) : null;
    }

    public static async Task<LocFile?> ReadAsync(string locsFilePath, string language, CancellationToken ct = default)
    {
        var entryName = GetEntryName(language);
        var data = await ZipHelper.ReadEntryAsync(locsFilePath, entryName, ct).ConfigureAwait(false);
        return data != null ? LocFileReader.Read(data) : null;
    }

    private static string GetEntryName(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            throw new ArgumentException("Language cannot be empty.", nameof(language));

        return language.EndsWith($".{PatchConst.LocFile}", StringComparison.OrdinalIgnoreCase)
            ? language
            : $"{language}.{PatchConst.LocFile}";
    }
}
