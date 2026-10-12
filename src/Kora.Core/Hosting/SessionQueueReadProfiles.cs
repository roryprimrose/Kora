using Kora.Core.Tools;

namespace Kora.Core.Hosting;

/// <summary>Closed host-owned reads with at most one exact catalogue selection, never arbitrary arguments.</summary>
public static class SessionQueueReadProfiles
{
    public static string CapabilityId(SessionQueueReadProfile profile) => profile switch
    {
        SessionQueueReadProfile.Version => ReadOnlyCapabilityCatalog.Version,
        SessionQueueReadProfile.Capabilities => ReadOnlyCapabilityCatalog.List,
        SessionQueueReadProfile.Readiness => ReadOnlyCapabilityCatalog.Readiness,
        SessionQueueReadProfile.Runtimes => ReadOnlyCapabilityCatalog.RuntimeList,
        SessionQueueReadProfile.Capability => ReadOnlyCapabilityCatalog.Get,
        SessionQueueReadProfile.RuntimeStatus => ReadOnlyCapabilityCatalog.RuntimeStatus,
        _ => throw new InvalidDataException("The fixed queue read profile is unknown."),
    };

    public static SessionQueueReadProfile? Parse(string value) => value.ToLowerInvariant() switch
    {
        "version" => SessionQueueReadProfile.Version,
        ReadOnlyCapabilityCatalog.List => SessionQueueReadProfile.Capabilities,
        ReadOnlyCapabilityCatalog.Readiness => SessionQueueReadProfile.Readiness,
        ReadOnlyCapabilityCatalog.RuntimeList => SessionQueueReadProfile.Runtimes,
        ReadOnlyCapabilityCatalog.Get => SessionQueueReadProfile.Capability,
        ReadOnlyCapabilityCatalog.RuntimeStatus => SessionQueueReadProfile.RuntimeStatus,
        _ => null,
    };

    public static void Validate(SessionQueueReadProfile profile, SessionQueueCapabilityTarget? target = null)
    {
        _ = CapabilityId(profile);
        if (profile == SessionQueueReadProfile.Capability)
        {
            _ = (target ?? throw new InvalidDataException("An explicit exact descriptor target is required.")).Descriptor;
        }
        else if (target is not null)
        {
            throw new InvalidDataException("Only capabilities.get can carry a descriptor target.");
        }
    }

    public static string Input(SessionQueueReadProfile profile, SessionQueueCapabilityTarget? target = null)
    {
        Validate(profile, target);
        return profile switch
        {
            SessionQueueReadProfile.Capability => CapabilityReplyWire.SerializeInput(new(target!.Descriptor.Id)),
            SessionQueueReadProfile.RuntimeStatus => CapabilityReplyWire.SerializeInput(new(ReadOnlyCapabilityCatalog.LocalRuntimeId)),
            _ => "{}",
        };
    }

    public static string Describe(SessionQueueReadProfile profile, SessionQueueCapabilityTarget? target = null)
    {
        Validate(profile, target);
        return CapabilityId(profile) + (profile switch
        {
            SessionQueueReadProfile.Capability => " | target " + target!.Descriptor.Id,
            SessionQueueReadProfile.RuntimeStatus => " | target " + ReadOnlyCapabilityCatalog.LocalRuntimeId,
            _ => "",
        });
    }

    public static bool Accepts(SessionQueueReadProfile profile, CapabilityReply reply, SessionQueueCapabilityTarget? target = null)
    {
        Validate(profile, target);
        if (reply.Outcome != CapabilityOutcome.Succeeded
            || !CapabilityReplyWire.IsWithinLimit(reply))
        {
            return false;
        }
        if (profile == SessionQueueReadProfile.Capability)
        {
            return CapabilityReplyWire.IsSingleDescriptor(reply, target!.Descriptor);
        }
        if (profile == SessionQueueReadProfile.RuntimeStatus)
        {
            return CapabilityReplyWire.IsSingleRecordedRuntime(reply);
        }
        if (reply.Descriptor is not null || reply.Runtime is not null) { return false; }
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
