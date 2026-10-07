using Kora.Core.Tools;

namespace Kora.Tools.Capabilities;

public sealed class CapabilitiesList
{
    internal CapabilityReply Execute(CapabilityPageInput input)
    {
        var total = ReadOnlyCapabilityCatalog.Descriptors.Count;
        if (ReadOnlyPage.Resolve(input, total) is not { } page) { return new(CapabilityOutcome.Denied, "page-out-of-range"); }
        return new(CapabilityOutcome.Succeeded, "admitted-read-only",
            Capabilities: new(ReadOnlyCapabilityCatalog.Descriptors.Skip(input.Offset).Take(page.Count).ToArray(), total, page.NextOffset));
    }
}
