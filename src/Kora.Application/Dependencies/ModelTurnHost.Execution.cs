using System.Runtime.CompilerServices;

using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost : IModelTurnExecution
{
    private sealed record ConsumerAdmission(Func<bool> Admitted, Func<CancellationToken, Task>? ValidateSource);
    private readonly ConditionalWeakTable<ModelTurn, ConsumerAdmission> consumerAdmissions = new();

    async Task<ModelTurnResult> IModelTurnExecution.RunAsync(ModelTurn turn, Func<bool> admitted, CancellationToken token,
        Func<CancellationToken, Task>? validateSource)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(admitted);
        if (turn.Owner != owner
            || turn.ResourceLease is null && !consumerAdmissions.TryAdd(turn, new(admitted, validateSource)))
        { return new(ModelTurnOutcome.Denied, ModelTurnReason.HostContextRequired); }
        if (turn.ResourceLease is not null)
        {
            consumerAdmissions.Remove(turn);
            consumerAdmissions.Add(turn, new(admitted, validateSource));
        }
        // The fence remains attached to the single-use capability, including any late adapter/tool callback.
        return await RunAsync(turn, token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task WaitForQuiescenceAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task outstanding;
            lock (dispatchGate)
            {
                if (IsQuiescent) { return; }
                if (quiescence is not null && !quiescence.Task.IsCompleted) { outstanding = quiescence.Task; }
                else if (!resources.IsIdle) { outstanding = resources.WaitForQuiescenceAsync(cancellationToken); }
                else { throw new InvalidOperationException("Provider termination is unconfirmed; the host remains unavailable."); }
            }
            await outstanding.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private bool ConsumerAdmissionCurrent(ModelTurn turn) =>
        !consumerAdmissions.TryGetValue(turn, out var consumer) || consumer.Admitted();

    private async Task ValidateConsumerSourceAsync(ModelTurn turn, CancellationToken token)
    {
        if (consumerAdmissions.TryGetValue(turn, out var consumer) && consumer.ValidateSource is { } validate)
        { await validate(token).ConfigureAwait(false); }
    }
}