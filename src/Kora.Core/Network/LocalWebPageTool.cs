using Kora.Core.Tools;

namespace Kora.Core.Network;

/// <summary>The one-page execution subset is separate from diagnostic discovery.</summary>
public static class LocalWebPageTool
{
    public static CapabilityDescriptor Descriptor { get; } = new(
        WebPageCapability.Id, WebPageCapability.SchemaVersion,
        CapabilityInputShape.WebPageAddress, CapabilityOutputShape.WebPage,
        CapabilityEffect.BoundedNetworkRead, CapabilityAvailability.Available,
        Array.AsReadOnly<CapabilityLane>([CapabilityLane.Execution]),
        new(WebPageCapability.MaximumInputUtf8Bytes, 1, 2048));

    public static IReadOnlyList<CapabilityDescriptor> Descriptors { get; } =
        Array.AsReadOnly<CapabilityDescriptor>([Descriptor]);

    public static CapabilityDescriptor? FindExact(string id) =>
        string.Equals(id, Descriptor.Id, StringComparison.Ordinal) ? Descriptor : null;

    public static bool Matches(CapabilityReply reply, Uri address) =>
        reply.WebPage is { LocalOnly: true } page
        && page.RequestedAddress == address && page.FinalAddress == address
        && page.Request is not null && page.RetrievedAt.Offset == TimeSpan.Zero
        && page.MediaType is "text/plain" or "text/html"
        && page.Text is not null
        && reply.Capabilities is null && reply.Descriptor is null && reply.Version is null
        && reply.Readiness is null && reply.Runtimes is null && reply.Runtime is null
        && reply.Sessions is null
        && CapabilityReplyWire.GetSerializedSize(reply) <= Descriptor.Limits.MaximumOutputUtf8Bytes;
}
