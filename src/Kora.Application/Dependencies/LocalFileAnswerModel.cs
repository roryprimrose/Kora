using Kora.Core.Context;
using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed class LocalFileAnswerModel(ModelTurnHost host) : ILocalFileAnswerModel
{
    public async Task<ModelTurnResult> RunAsync(ModelContextEnvelope context, Func<bool> isCurrent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!isCurrent()) { return new(ModelTurnOutcome.Denied, ModelTurnReason.HostAdmissionClosed); }
        var admission = await host.AdmitAsync(ModelTurnChoice.Local, context, null, cancellationToken).ConfigureAwait(false);
        if (admission.Turn is not { } turn) { return admission.Result; }
        cancellationToken.ThrowIfCancellationRequested();
        if (!isCurrent()) { return new(ModelTurnOutcome.Denied, ModelTurnReason.HostAdmissionClosed); }
        var result = await host.RunAsync(turn, cancellationToken).ConfigureAwait(false);
        return isCurrent() && !cancellationToken.IsCancellationRequested ? result
            : new(ModelTurnOutcome.Denied, ModelTurnReason.HostAdmissionClosed);
    }
}
