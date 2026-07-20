using System;
using System.Collections.Generic;

namespace SP.Resource.Localization;

public sealed class LocFile(string language)
{
    public string Language { get; } = language;
    public Dictionary<string, string> Map { get; } = new(StringComparer.OrdinalIgnoreCase);
}
