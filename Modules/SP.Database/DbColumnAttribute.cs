namespace SP.Database;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class DbColumnAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
    public bool IgnoreRead { get; set; }
    public bool IgnoreWrite { get; set; }
}
