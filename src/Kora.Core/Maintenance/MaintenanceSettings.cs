using System.Globalization;

namespace Kora.Core.Maintenance;

public sealed record MaintenanceSettings
{
    public const int MinimumIntervalHours = 1;
    public const int MaximumIntervalHours = 168;
    public const int DefaultIntervalHours = 6;
    public const int MaximumBytes = 64;

    public static MaintenanceSettings Default { get; } = new(false, false, DefaultIntervalHours);

    public MaintenanceSettings(bool networkEnabled, bool includePrereleases, int checkIntervalHours)
    {
        ValidateInterval(checkIntervalHours);
        NetworkEnabled = networkEnabled;
        IncludePrereleases = includePrereleases;
        CheckIntervalHours = checkIntervalHours;
    }

    public bool NetworkEnabled { get; }
    public bool IncludePrereleases { get; }
    public int CheckIntervalHours { get; }

    public static void ValidateInterval(int hours)
    {
        if (hours is < MinimumIntervalHours or > MaximumIntervalHours)
        {
            throw new InvalidDataException("Maintenance check interval must be between 1 and 168 whole hours.");
        }
    }

    public string Serialize() => string.Create(CultureInfo.InvariantCulture,
        $"v1,{(NetworkEnabled ? 1 : 0)},{(IncludePrereleases ? 1 : 0)},{CheckIntervalHours}");

    public static MaintenanceSettings Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var fields = text.Split(',');
        if (fields.Length != 4 || !string.Equals(fields[0], "v1", StringComparison.Ordinal)
            || fields[1] is not ("0" or "1") || fields[2] is not ("0" or "1")
            || !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !string.Equals(fields[3], hours.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            throw new InvalidDataException("The saved maintenance settings are invalid.");
        }
        return new(string.Equals(fields[1], "1", StringComparison.Ordinal),
            string.Equals(fields[2], "1", StringComparison.Ordinal), hours);
    }
}
