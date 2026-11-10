namespace Kora.Core.Tools;

public static class SessionMetadataTool
{
    public const string Id = "sessions.get";
    public const string ResultReason = "reviewed-exact-content-free-session";
    public static CapabilityDescriptor Descriptor { get; } = new(Id, 1, CapabilityInputShape.SessionId,
        CapabilityOutputShape.SessionMetadata, CapabilityEffect.ReadOnlyObservation, CapabilityAvailability.Available,
        Array.AsReadOnly<CapabilityLane>([CapabilityLane.Execution]), new(128, 1, 4096));
    public static IReadOnlyList<CapabilityDescriptor> Descriptors { get; } =
        Array.AsReadOnly<CapabilityDescriptor>([Descriptor]);

    public static bool Matches(CapabilityReply reply, string id)
    {
        if (reply.Outcome != CapabilityOutcome.Succeeded || !string.Equals(reply.Reason, ResultReason, StringComparison.Ordinal)
            || reply.SessionMetadata is not { } metadata || !string.Equals(metadata.SessionId, id, StringComparison.Ordinal)
            || reply.Capabilities is not null || reply.Descriptor is not null || reply.Version is not null
            || reply.Readiness is not null || reply.Runtimes is not null || reply.Runtime is not null
            || reply.WebPage is not null || reply.Mcp is not null || reply.Sessions is not null
            || reply.Customizations is not null || reply.Settings is not null || reply.SessionMetrics is not null
            || reply.ProviderSettings is not null || reply.Maintenance is not null)
        { return false; }
        metadata.Validate();
        return CapabilityReplyWire.GetSerializedSize(reply) <= Descriptor.Limits.MaximumOutputUtf8Bytes;
    }
}
