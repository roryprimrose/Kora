using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

/// <summary>Host-issued single admission, not physical transmission consent.</summary>
public abstract class HostedTurnAuthorization
{
    private protected HostedTurnAuthorization(HostedTransmissionReviewService owner, HostActivity issuer,
        ModelContextEnvelope context, ProviderProfileTuple profile)
    { Owner = owner; Issuer = issuer; Context = context; Profile = profile; }

    public static TimeSpan MaximumDuration => TimeSpan.FromMinutes(2);
    internal HostedTransmissionReviewService Owner { get; }
    internal HostActivity Issuer { get; }
    internal ModelContextEnvelope Context { get; }
    internal int Used;
    /// <summary>The admitted turn's clock; set once by the host when it binds this authorization to that turn.</summary>
    internal ModelTurnLifetime? Lifetime { get; set; }
    internal bool ContextCurrent(TimeProvider time) =>
        Context.CreatedAt <= time.GetUtcNow()
        && time.GetUtcNow() < (Lifetime?.Extend(Context.ExpiresAt) ?? Context.ExpiresAt);
    internal (Kora.Core.Storage.ISessionPermissionStore Store,
        Kora.Core.Authorization.SessionPermission.SessionPermissionPolicy Policy)? AutomaticPermission { get; set; }
    public ProviderProfileTuple Profile { get; }
}
