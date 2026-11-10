using Kora.Core.Tools;

namespace Kora.Tools.Sessions;

public sealed class SessionsGet
{
    public CapabilityReply Execute(SessionMetadataSnapshot reviewed, string input)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        reviewed.Validate();
        var parsed = CapabilityInputPolicy.Read(SessionMetadataTool.Descriptor, input);
        if (parsed.DenialReason is not null || !string.Equals(parsed.Id!.Id, reviewed.SessionId, StringComparison.Ordinal))
        { return new(CapabilityOutcome.Denied, parsed.DenialReason ?? "unreviewed-exact-session"); }
        var result = new CapabilityReply(CapabilityOutcome.Succeeded, SessionMetadataTool.ResultReason,
            SessionMetadata: reviewed);
        SessionMetadataTool.RequireMatch(result, reviewed.SessionId);
        return result;
    }
}
