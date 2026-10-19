namespace Kora.Core.Dependencies;

public sealed class ModelToolCall
{
    internal ModelToolCall(string callId, string capabilityId, string inputJson)
    {
        CallId = callId;
        CapabilityId = capabilityId;
        InputJson = inputJson;
    }

    public string CallId { get; }
    public string CapabilityId { get; }
    public string InputJson { get; }
}
