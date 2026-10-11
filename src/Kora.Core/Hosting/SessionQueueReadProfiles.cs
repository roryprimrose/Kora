using Kora.Core.Tools;

namespace Kora.Core.Hosting;

/// <summary>Closed host-owned reads. A profile never carries arguments, paging or executable content.</summary>
public static class SessionQueueReadProfiles
{
    public static string CapabilityId(SessionQueueReadProfile profile) => profile switch
    {
        SessionQueueReadProfile.Version => ReadOnlyCapabilityCatalog.Version,
        SessionQueueReadProfile.Capabilities => ReadOnlyCapabilityCatalog.List,
        SessionQueueReadProfile.Readiness => ReadOnlyCapabilityCatalog.Readiness,
        SessionQueueReadProfile.Runtimes => ReadOnlyCapabilityCatalog.RuntimeList,
        _ => throw new InvalidDataException("The fixed queue read profile is unknown."),
    };

    public static SessionQueueReadProfile? Parse(string value) => value.ToLowerInvariant() switch
    {
        "version" => SessionQueueReadProfile.Version,
        ReadOnlyCapabilityCatalog.List => SessionQueueReadProfile.Capabilities,
        ReadOnlyCapabilityCatalog.Readiness => SessionQueueReadProfile.Readiness,
        ReadOnlyCapabilityCatalog.RuntimeList => SessionQueueReadProfile.Runtimes,
        _ => null,
    };

    public static bool Accepts(SessionQueueReadProfile profile, CapabilityReply reply)
    {
        _ = CapabilityId(profile);
        if (reply.Outcome != CapabilityOutcome.Succeeded
            || CapabilityReplyWire.GetSerializedSize(reply) > ReadOnlyCapabilityCatalog.Limits.MaximumOutputUtf8Bytes
            || reply.Descriptor is not null || reply.Runtime is not null)
        {
            return false;
        }
        return profile switch
        {
            SessionQueueReadProfile.Version => reply.Version is not null
                && reply.Capabilities is null && reply.Readiness is null && reply.Runtimes is null,
            SessionQueueReadProfile.Capabilities => reply.Capabilities is { } page
                && FirstPage(page.Records?.Count, page.TotalRecords, page.NextOffset)
                && page.Records!.All(record => record is not null)
                && reply.Version is null && reply.Readiness is null && reply.Runtimes is null,
            SessionQueueReadProfile.Readiness => reply.Readiness is { } page
                && FirstPage(page.Records?.Count, page.TotalRecords, page.NextOffset)
                && page.Records!.All(record => record is not null)
                && reply.Version is null && reply.Capabilities is null && reply.Runtimes is null,
            _ => reply.Runtimes is { } page
                && FirstPage(page.Records?.Count, page.TotalRecords, page.NextOffset)
                && page.Records!.All(record => record is not null)
                && reply.Version is null && reply.Capabilities is null && reply.Readiness is null,
        };
    }

    private static bool FirstPage(int? count, int total, int? next) =>
        count is >= 0 and <= ReadOnlyCapabilityCatalog.MaximumRecords
        && total >= count && count == Math.Min(total, ReadOnlyCapabilityCatalog.MaximumRecords)
        && next == (total > count ? count : null);
}
