using Kora.Core.Tools;

namespace Kora.Tools.Customizations;

public sealed class CustomizationsList
{
    public CapabilityReply Execute(IReadOnlyList<CustomizationMetadata> reviewed, CapabilityPageInput input)
    {
        if (reviewed.Count is < 1 or > CustomizationDiscoveryTools.MaximumRecords
            || input.Offset < 0 || input.Count is < 1 or > CustomizationDiscoveryTools.MaximumRecords
            || CapabilityPagePolicy.Resolve(input, reviewed.Count) is not { } page)
        { return new(CapabilityOutcome.Denied, "reviewed-page-out-of-range"); }
        var reply = new CapabilityReply(CapabilityOutcome.Succeeded, CustomizationDiscoveryTools.ResultReason,
            Customizations: new(reviewed.Skip(input.Offset).Take(page.Count).ToArray(), reviewed.Count, page.NextOffset));
        return CapabilityReplyWire.IsWithinLimit(reply) ? reply : new(CapabilityOutcome.Failed, "output-limit-exceeded");
    }
}
