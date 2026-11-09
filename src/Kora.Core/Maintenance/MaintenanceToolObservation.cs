namespace Kora.Core.Maintenance;

public sealed record MaintenanceToolObservation(
    string CurrentVersion,
    ReleaseChannel Channel,
    ReleaseAvailability Availability,
    string Message,
    string? ReleaseVersion = null,
    InstallerStartOutcome? Installer = null);
