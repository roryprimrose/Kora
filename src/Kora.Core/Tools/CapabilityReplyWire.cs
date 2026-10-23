using System.Text.Json;
using System.Text.Json.Serialization;
using Kora.Core.Dependencies;

namespace Kora.Core.Tools;

/// <summary>The registry wire envelope is the authoritative serialized result bound.</summary>
public static class CapabilityReplyWire
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static byte[] Serialize(CapabilityReply reply) => JsonSerializer.SerializeToUtf8Bytes(reply, Options);
    public static int GetSerializedSize(CapabilityReply reply) => Serialize(reply).Length;
    public static bool IsWithinLimit(CapabilityReply reply) =>
        GetSerializedSize(reply) <= ReadOnlyCapabilityCatalog.Limits.MaximumOutputUtf8Bytes;
    public static string SerializeInput(CapabilityIdInput input) => JsonSerializer.Serialize(input, Options);

    public static bool IsSingleDescriptor(CapabilityReply reply, CapabilityDescriptor expected) =>
        reply.Descriptor is { } descriptor && IsExactDescriptor(descriptor, expected)
        && reply.Runtime is null && HasNoPages(reply);

    public static bool IsSingleRecordedRuntime(CapabilityReply reply) =>
        reply.Runtime is { } runtime && IsRecordedLocalRuntime(runtime)
        && reply.Descriptor is null && HasNoPages(reply);

    private static bool HasNoPages(CapabilityReply reply) =>
        reply.Version is null && reply.Capabilities is null && reply.Readiness is null && reply.Runtimes is null
        && reply.WebPage is null;

    public static bool IsExactDescriptor(CapabilityDescriptor descriptor, CapabilityDescriptor expected) =>
        string.Equals(descriptor.Id, expected.Id, StringComparison.Ordinal)
        && descriptor.SchemaVersion == expected.SchemaVersion && descriptor.Input == expected.Input
        && descriptor.Output == expected.Output && descriptor.Effect == expected.Effect
        && descriptor.Availability == expected.Availability && descriptor.Limits == expected.Limits
        && descriptor.CallerLanes is { } lanes && lanes.SequenceEqual(expected.CallerLanes);

    public static bool IsRecordedLocalRuntime(RuntimeObservation runtime) =>
        string.Equals(runtime.Id, ReadOnlyCapabilityCatalog.LocalRuntimeId, StringComparison.Ordinal)
        && runtime.Locality == RuntimeLocality.Local && !runtime.ToolLoopQualified
        && !string.IsNullOrWhiteSpace(runtime.Reason)
        && (runtime.Readiness is null
            ? runtime.Availability == CapabilityAvailability.NotObserved && runtime.ObservedAt is null
            : Enum.IsDefined(runtime.Readiness.Value) && runtime.ObservedAt is { } observed && observed.Offset == TimeSpan.Zero
                && runtime.Availability == (runtime.Readiness == DependencyReadiness.Ready
                    ? CapabilityAvailability.Available : CapabilityAvailability.Unavailable));
}
