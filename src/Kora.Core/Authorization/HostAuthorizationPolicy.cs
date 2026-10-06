namespace Kora.Core.Authorization;

// The admitted host resolves these gates inside the storage transaction, not from reply/provider fields.
public sealed record HostAuthorizationPolicy(
    bool IsUnlocked,
    bool OtherMandatoryGatesSatisfied,
    bool IsProtectedCall,
    bool IgnoreReusableGrants);
