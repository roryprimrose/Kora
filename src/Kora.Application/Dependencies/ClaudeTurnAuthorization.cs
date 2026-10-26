using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

public sealed class ClaudeTurnAuthorization : HostedTurnAuthorization
{
    internal ClaudeTurnAuthorization(HostedTransmissionReviewService owner, HostActivity issuer,
        ModelContextEnvelope context, ProviderProfileTuple profile, bool streamAnswer = false)
        : base(owner, issuer, context, profile) { StreamAnswer = streamAnswer; }

    internal bool StreamAnswer { get; }
}
