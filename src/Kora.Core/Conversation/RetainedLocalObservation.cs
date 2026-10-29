using System.Text;

using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Core.Conversation;

/// <summary>Original host observations, not current truth or reusable execution authority.</summary>
public sealed record RetainedLocalObservation(Guid Id, string CapabilityId, string InputJson,
    DateTimeOffset ObservedAt, string ResultJson)
{
    public static RetainedLocalObservation Capture(string capabilityId, string inputJson,
        CapabilityReply reply, DateTimeOffset observedAt)
    {
        RequireSupported(capabilityId);
        var result = ModelToolProtocol.CreateToolResult(new("host-observation", capabilityId, inputJson), reply);
        var observation = new RetainedLocalObservation(Guid.NewGuid(), capabilityId, inputJson, observedAt,
            Encoding.UTF8.GetString(result.Result.AsSpan()));
        observation.Validate();
        return observation;
    }

    public void Validate()
    {
        RequireSupported(CapabilityId);
        if (Id == Guid.Empty || ObservedAt.Offset != TimeSpan.Zero
            || InputJson is null || ResultJson is null)
        { throw new InvalidDataException("Retained observation identity, time or source is invalid."); }
        ModelToolProtocol.ValidateRetainedReadOnlyResult(CapabilityId, InputJson, ResultJson);
    }

    private static void RequireSupported(string id)
    {
        if (id is not (ReadOnlyCapabilityCatalog.List or ReadOnlyCapabilityCatalog.Get
            or ReadOnlyCapabilityCatalog.Version or ReadOnlyCapabilityCatalog.Readiness
            or ReadOnlyCapabilityCatalog.RuntimeList or ReadOnlyCapabilityCatalog.RuntimeStatus))
        { throw new InvalidDataException("Only the six original local host observations may be retained."); }
    }
}
