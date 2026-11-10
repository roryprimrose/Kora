using System.Text.Json;
using Kora.Core.Maintenance;

namespace Kora.Core.Tools;

public static class MaintenanceTools
{
    public const string Check = "application.check_for_updates";
    public const string Upgrade = "application.upgrade";
    public const string ResultReason = "canonical-application-maintenance";
    public static readonly TimeSpan Deadline = TimeSpan.FromMinutes(20);
    public const string ModelPolicy =
        "This explicit application maintenance request permits only application.check_for_updates and application.upgrade. "
        + "Use {} for latest or {version} for an exact major.minor.patch or major.minor.patch-betaN version. "
        + "Never supply URLs, paths, scripts, channel changes or approval fields. Checking uses saved network consent. "
        + "Upgrade requires the original user's exact native unsigned-installer approval, then starts only the verified installer. "
        + "The installer owns running-instance shutdown and setup. Started is not installed; never claim completed installation, "
        + "approve on the user's behalf, change consent or start another installer after an unconfirmed outcome.";

    public static IReadOnlyList<CapabilityDescriptor> Descriptors { get; } = Array.AsReadOnly(
    [
        new CapabilityDescriptor(Check, 1, CapabilityInputShape.ReleaseVersion, CapabilityOutputShape.Maintenance,
            CapabilityEffect.BoundedNetworkRead, CapabilityAvailability.Available,
            Array.AsReadOnly<CapabilityLane>([CapabilityLane.Native, CapabilityLane.Execution]), ReadOnlyCapabilityCatalog.Limits),
        new CapabilityDescriptor(Upgrade, 1, CapabilityInputShape.ReleaseVersion, CapabilityOutputShape.Maintenance,
            CapabilityEffect.HostInstallerLaunch, CapabilityAvailability.Available,
            Array.AsReadOnly<CapabilityLane>([CapabilityLane.Native, CapabilityLane.Execution]), ReadOnlyCapabilityCatalog.Limits),
    ]);

    public static CapabilityDescriptor? FindExact(string id) =>
        Descriptors.SingleOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));

    public static bool IsExplicitRequest(string body) => MaintenanceUpdateCommand.Parse(body, "Kora") is { IsValid: true };
    public static string CreateInput(ReleaseVersion? version) =>
        version is null ? "{}" : JsonSerializer.Serialize(new { version = version.ToString() });

    public static ReleaseVersion? ReadVersion(JsonElement root)
    {
        var fields = root.EnumerateObject().ToArray();
        if (fields.Length == 0) { return null; }
        if (fields.Length != 1 || !string.Equals(fields[0].Name, "version", StringComparison.Ordinal)
            || fields[0].Value.ValueKind != JsonValueKind.String || fields[0].Value.GetString()!.Length > 64)
        { throw new InvalidDataException("Maintenance accepts only an exact version, never a path, URL or approval."); }
        return ReleaseVersion.Parse(fields[0].Value.GetString()!);
    }

    public static bool Matches(CapabilityReply reply, string id, ReleaseVersion? requested = null)
    {
        if (reply.Capabilities is not null || reply.Descriptor is not null || reply.Version is not null
            || reply.Readiness is not null || reply.Runtimes is not null || reply.Runtime is not null
            || reply.WebPage is not null || reply.Mcp is not null || reply.Sessions is not null
            || reply.Customizations is not null || reply.Settings is not null || reply.SessionMetrics is not null
            || reply.ProviderSettings is not null || reply.SessionMetadata is not null)
        { return false; }
        if (reply.Maintenance is not { } result) { return reply.Outcome != CapabilityOutcome.Succeeded; }
        if (!string.Equals(reply.Reason, ResultReason, StringComparison.Ordinal)
            || !Enum.IsDefined(result.Channel) || !Enum.IsDefined(result.Availability)
            || result.Installer is { } installer && !Enum.IsDefined(installer)
            || string.IsNullOrWhiteSpace(result.Message) || result.Message.Length > MaintenanceCommandParser.MaximumOutputLength)
        { return false; }
        try
        {
            _ = ReleaseVersion.Parse(result.CurrentVersion);
            if (result.ReleaseVersion is { } version) { _ = ReleaseVersion.Parse(version); }
        }
        catch (InvalidDataException) { return false; }
        return reply.Outcome != CapabilityOutcome.Succeeded
            || (requested is null || string.Equals(result.ReleaseVersion, requested.ToString(), StringComparison.Ordinal))
            && (id switch
        {
            Check => result.Installer is null && result.Availability is ReleaseAvailability.Available or ReleaseAvailability.UpToDate,
            Upgrade => result.Installer == InstallerStartOutcome.Started && result.Availability == ReleaseAvailability.Available
                && result.ReleaseVersion is not null,
            _ => false,
        });
    }
}
