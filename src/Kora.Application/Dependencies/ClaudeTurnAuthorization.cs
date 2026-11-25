using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

public sealed class ClaudeTurnAuthorization : HostedTurnAuthorization
{
    internal ClaudeTurnAuthorization(HostedTransmissionReviewService owner, HostActivity issuer,
        ModelContextEnvelope context, ProviderProfileTuple profile, bool streamAnswer = false,
        SelectedHostedDefinition? selection = null, Func<CancellationToken, ValueTask<bool>>? sourceCurrent = null)
        : base(owner, issuer, context, profile)
    { StreamAnswer = streamAnswer; Selection = selection; SourceCurrent = sourceCurrent; }

    internal bool StreamAnswer { get; }
    internal SelectedHostedDefinition? Selection { get; }
    internal Func<CancellationToken, ValueTask<bool>>? SourceCurrent { get; }
}
