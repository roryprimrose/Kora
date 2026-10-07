using System.Globalization;
using System.Text.RegularExpressions;

namespace Kora.Core.Maintenance;

public sealed partial record ReleaseVersion(int Major, int Minor, int Patch, int? Beta) : IComparable<ReleaseVersion>
{
    public static ReleaseVersion Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var match = VersionPattern().Match(text);
        if (!match.Success) { throw new InvalidDataException("Version is not a canonical GitVersion stable/beta release."); }
        try
        {
            return new(int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["patch"].Value, CultureInfo.InvariantCulture),
                match.Groups["beta"].Success ? int.Parse(match.Groups["beta"].Value, CultureInfo.InvariantCulture) : null);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Release version exceeds the numeric bound.", exception);
        }
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) { return 1; }
        var comparison = Major.CompareTo(other.Major);
        if (comparison == 0) { comparison = Minor.CompareTo(other.Minor); }
        if (comparison == 0) { comparison = Patch.CompareTo(other.Patch); }
        if (comparison != 0) { return comparison; }
        if (Beta is null) { return other.Beta is null ? 0 : 1; }
        return other.Beta is null ? -1 : Beta.Value.CompareTo(other.Beta.Value);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Major}.{Minor}.{Patch}") + (Beta is { } beta ? "-beta" + beta.ToString(CultureInfo.InvariantCulture) : string.Empty);

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    [GeneratedRegex(@"^(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-beta(?<beta>0|[1-9][0-9]*))?\z",
        RegexOptions.CultureInvariant, 100)]
    private static partial Regex VersionPattern();
}
