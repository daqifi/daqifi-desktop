using System.Text.RegularExpressions;

namespace Daqifi.Desktop.Helpers;

public static class VersionHelper
{
    private static readonly Regex FullVersionRegex = new(
        @"(?ix)^\s* v?\s*
          (?<maj>\d+) (?:\.(?<min>\d+))? (?:\.(?<pat>\d+))?  # numeric core
          (?<suffix> [A-Za-z]+ \d* )?                           # optional prerelease like b2/rc1
          \s*$",
        RegexOptions.Compiled);

    public readonly record struct VersionInfo(int Major, int Minor, int Patch, string? PreLabel, int PreNumber) : IComparable<VersionInfo>
    {
        public bool IsPreRelease => !string.IsNullOrEmpty(PreLabel);

        public int CompareTo(VersionInfo other)
        {
            var cmp = Major.CompareTo(other.Major);
            if (cmp != 0) return cmp;
            cmp = Minor.CompareTo(other.Minor);
            if (cmp != 0) return cmp;
            cmp = Patch.CompareTo(other.Patch);
            if (cmp != 0) return cmp;

            // Numeric equal; compare prerelease precedence
            var thisRank = GetPrecedenceRank(PreLabel);
            var otherRank = GetPrecedenceRank(other.PreLabel);
            if (thisRank != otherRank) return thisRank.CompareTo(otherRank);
            // Same label
            return PreNumber.CompareTo(other.PreNumber);
        }

        private static int GetPrecedenceRank(string? label)
        {
            if (string.IsNullOrEmpty(label)) return 3; // release highest
            label = label.ToLowerInvariant();
            if (label is "rc" or "releasecandidate") return 2;
            if (label is "b" or "beta") return 1;
            if (label is "a" or "alpha" or "pre" or "preview" or "dev") return 0;
            // Unknown label: treat as very early prerelease
            return 0;
        }

        public override string ToString()
        {
            var core = $"{Major}.{Minor}.{Patch}";
            return IsPreRelease ? core + PreLabel + (PreNumber > 0 ? PreNumber.ToString() : string.Empty) : core;
        }
    }

    public static bool TryParseVersionInfo(string? input, out VersionInfo version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var m = FullVersionRegex.Match(input.Trim());
        if (!m.Success) return false;

        var major = int.Parse(m.Groups["maj"].Value);
        var minor = int.TryParse(m.Groups["min"].Value, out var mi) ? mi : 0;
        var patch = int.TryParse(m.Groups["pat"].Value, out var pa) ? pa : 0;

        var suffix = m.Groups["suffix"].Success ? m.Groups["suffix"].Value : null;
        string? label = null;
        var preNum = 0;
        if (!string.IsNullOrEmpty(suffix))
        {
            // Split letters and trailing digits
            var i = suffix.TakeWhile(char.IsLetter).Count();
            label = suffix.Substring(0, i);
            var numPart = suffix.Substring(i);
            _ = int.TryParse(numPart, out preNum);
        }

        version = new VersionInfo(major, minor, patch, label, preNum);
        return true;
    }

    public static int Compare(string? left, string? right)
    {
        var hasL = TryParseVersionInfo(left, out var l);
        var hasR = TryParseVersionInfo(right, out var r);
        if (!hasL && !hasR) return 0;
        if (!hasL) return -1;
        if (!hasR) return 1;
        return l.CompareTo(r);
    }

    // Compatibility helper for older call sites
    public static string? NormalizeVersionString(string? input)
    {
        if (!TryParseVersionInfo(input, out var v)) return null;
        var suffix = v.IsPreRelease ? v.PreLabel + (v.PreNumber > 0 ? v.PreNumber.ToString() : string.Empty) : string.Empty;
        return $"{v.Major}.{v.Minor}.{v.Patch}{suffix}";
    }
}


