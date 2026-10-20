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
        if (turn.Owner != owner || !consumerAdmissions.TryAdd(turn, new(admitted, validateSource)))
        { return new(ModelTurnOutcome.Denied, ModelTurnReason.HostContextRequired); }
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
                outstanding = quiescence!.Task;
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
