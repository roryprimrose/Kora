using Kora.Core.Tools;

namespace Kora.Tools.Customizations;

public sealed class CustomizationsGet
{
    public CapabilityReply Execute(IReadOnlyList<CustomizationMetadata> reviewed, CapabilityIdInput input)
    {
        var record = reviewed.SingleOrDefault(item => string.Equals(item.Ref, input.Id, StringComparison.Ordinal));
        return record is null ? new(CapabilityOutcome.Denied, "unknown-reviewed-reference")
            : new(CapabilityOutcome.Succeeded, CustomizationDiscoveryTools.ResultReason,
                Customizations: new([record], 1, null));
    }
}
