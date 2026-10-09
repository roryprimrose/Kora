using System.Globalization;
using System.Runtime.InteropServices;

namespace Kora.Core.Configuration;

[StructLayout(LayoutKind.Auto)]
public readonly record struct SessionRetentionSettings
{
    public const int MaximumDays = 365;
    public static SessionRetentionSettings Default => new(1, 30);

    public SessionRetentionSettings(int archiveDays, int deleteDays)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(archiveDays, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(deleteDays, MaximumDays);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(deleteDays, archiveDays);
        ArchiveDays = archiveDays;
        DeleteDays = deleteDays;
    }

    public int ArchiveDays { get; }
    public int DeleteDays { get; }

    public void Validate() => _ = new SessionRetentionSettings(ArchiveDays, DeleteDays);

    public static SessionRetentionSettings Parse(string archive, string delete) => new(ParseDays(archive), ParseDays(delete));

    private static int ParseDays(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var days)
            || !string.Equals(value, days.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Use canonical positive integer days, with archive before deletion and deletion no later than 365 days.");
        }
        return days;
    }
}
