using Kora.Core.Tools;

namespace Kora.Tools.Runtime;

public sealed class RuntimeGetStatus(RecordedRuntimeObservation observation)
{
    internal CapabilityReply Execute(CapabilityIdInput input) =>
        string.Equals(input.Id, "local.inference", StringComparison.Ordinal)
            ? new(CapabilityOutcome.Succeeded, "recorded-observation", Runtime: observation.Read())
            : new(CapabilityOutcome.Denied, "unknown-runtime");
}
