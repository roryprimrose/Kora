using Kora.Core.Tools;

namespace Kora.Core.Dependencies;

public static partial class ModelToolProtocol
{
    /// <summary>Apply the existing exact input/result policy without creating or enabling a model turn.</summary>
    public static void ValidateObservationResult(string id, string input, CapabilityReply reply)
    {
        if (ReadOnlyCapabilityCatalog.FindExact(id) is not { } descriptor
            || CapabilityInputPolicy.Read(descriptor, input).DenialReason is not null)
        { throw new InvalidDataException("Only a complete original six-ID observation is admitted."); }
        _ = CreateToolResult(new("host-observation", id, input), reply);
    }
}
