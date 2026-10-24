using System.Collections.Immutable;

namespace Kora.Core.Dependencies;

/// <summary>Exact bounded host result bytes, structurally separate from instructions and user intent.</summary>
public sealed class ModelToolResultMessage : ModelToolMessage
{
    internal ModelToolResultMessage(string callId, string capabilityId, ImmutableArray<byte> result)
    {
        CallId = callId;
        CapabilityId = capabilityId;
        Result = result;
    }

    public string CallId { get; }
    public string CapabilityId { get; }
    public ImmutableArray<byte> Result { get; }
}
