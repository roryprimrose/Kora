using System.Collections.Immutable;

using Kora.Core.Tools;

namespace Kora.Tools.Sessions;

public sealed class SessionsList
{
    internal CapabilityReply Execute(ImmutableArray<SessionDescriptor> reviewed, CapabilityPageInput input)
    {
        if (reviewed.IsDefault || reviewed.Length > SessionDiscoveryTool.MaximumRecords
            || !reviewed.All(SessionDiscoveryTool.IsValidRecord)
            || reviewed.Select(record => record.SessionId).Distinct(StringComparer.Ordinal).Count() != reviewed.Length)
        { return new(CapabilityOutcome.Failed, "invalid-reviewed-session-metadata"); }
        if (input.Offset < 0 || input.Count is < 1 or > SessionDiscoveryTool.MaximumRecords
            || SessionDiscoveryTool.Resolve(input, reviewed.Length) is not { } page)
        { return new(CapabilityOutcome.Denied, "page-out-of-range"); }
        var reply = new CapabilityReply(CapabilityOutcome.Succeeded, SessionDiscoveryTool.ResultReason,
            Sessions: new(reviewed.Skip(input.Offset).Take(page.Count).ToImmutableArray(), reviewed.Length, page.NextOffset));
        return CapabilityReplyWire.IsWithinLimit(reply) ? reply : new(CapabilityOutcome.Failed, "output-limit-exceeded");
    }
}
