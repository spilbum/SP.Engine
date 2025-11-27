using System;
using System.Linq;

namespace SP.Shared.Resource;


public readonly struct BuildVersion : IComparable<BuildVersion>, IEquatable<BuildVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int BuildNum { get; }

    public BuildVersion(int major, int minor, int buildNum)
    {
        if (major < 0) throw new ArgumentOutOfRangeException(nameof(major));
        if (minor < 0) throw new ArgumentOutOfRangeException(nameof(minor));
        if (buildNum < 0) throw new ArgumentOutOfRangeException(nameof(buildNum));
        
        Major = major;
        Minor = minor;
        BuildNum = buildNum;
    }

    public static BuildVersion Parse(string s)
    {
        if (s == null) throw new ArgumentNullException(nameof(s));
        if (!TryParse(s, out var v))
            throw new FormatException($"Invalid build version: '{s}'");
        return v;
    }

    public static bool TryParse(string s, out BuildVersion version)
    {
        version = default;
        
        if (string.IsNullOrWhiteSpace(s))
            return false;

        var clean = new string(s.Trim()
            .Where(ch => char.IsDigit(ch) || ch == '.')
            .ToArray());
        
        if (string.IsNullOrEmpty(clean))
            return false;

        var parts = clean.Split('.');
        if (parts.Length == 0)
            return false;
        
        int minor = 0, buildNum = 0;
        
        if (!int.TryParse(parts[0], out var major))
            return false;
        
        switch (parts.Length)
        {
            case > 1 when !int.TryParse(parts[1], out minor):
            case > 2 when !int.TryParse(parts[2], out buildNum):
                return false;
            default:
                version = new BuildVersion(major, minor, buildNum);
                return true;
        }
    }

    public int CompareTo(BuildVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        
        c = Minor.CompareTo(other.Minor);
        return c != 0 ? c : BuildNum.CompareTo(other.BuildNum);
    }

    public bool Equals(BuildVersion other)
        => Major == other.Major && Minor == other.Minor && BuildNum == other.BuildNum;

    public override bool Equals(object? obj)
        => obj is BuildVersion v && Equals(v);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + Major;
            hash = hash * 31 + Minor;
            hash = hash * 31 + BuildNum;
            return hash;
        }
    }

    public static bool operator ==(BuildVersion left, BuildVersion right) => left.Equals(right);
    public static bool operator !=(BuildVersion left, BuildVersion right) => !left.Equals(right);
    public static bool operator <(BuildVersion left, BuildVersion right) => left.CompareTo(right) < 0;
    public static bool operator <=(BuildVersion left, BuildVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >(BuildVersion left, BuildVersion right) => left.CompareTo(right) > 0;
    public static bool operator >=(BuildVersion left, BuildVersion right) => left.CompareTo(right) >= 0;
    
    public override string ToString()
        => $"{Major}.{Minor}.{BuildNum}";
    
    public bool IsZero => Major == 0 && Minor == 0 && BuildNum == 0;
}

