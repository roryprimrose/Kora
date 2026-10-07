using Kora.Core.Tools;
using Kora.Tools.Capabilities;

namespace Kora.Tools.Runtime;

public sealed class RuntimeList(RecordedRuntimeObservation observation)
{
    internal CapabilityReply Execute(CapabilityPageInput input)
    {
        const int total = 1;
        if (ReadOnlyPage.Resolve(input, total) is not { } page) { return new(CapabilityOutcome.Denied, "page-out-of-range"); }
        return new(CapabilityOutcome.Succeeded, "recorded-observation",
            Runtimes: new([observation.Read()], total, page.NextOffset));
    }
}
