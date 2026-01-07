using System;
using System.Collections.Generic;

namespace SP.Shared.Resource.Localization;

public sealed class LocFile(string language)
{
    public string Language { get; } = language;
    public Dictionary<string, string> Map { get; } = new(StringComparer.OrdinalIgnoreCase);
}
