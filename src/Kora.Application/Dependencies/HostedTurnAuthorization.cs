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
    internal (Kora.Core.Storage.ISessionPermissionStore Store,
        Kora.Core.Authorization.SessionPermission.SessionPermissionPolicy Policy)? AutomaticPermission { get; set; }
    public ProviderProfileTuple Profile { get; }
}
