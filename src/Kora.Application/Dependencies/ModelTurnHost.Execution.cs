using System.Runtime.CompilerServices;

using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost : IModelTurnExecution
{
    private sealed record ConsumerAdmission(Func<bool> Admitted, Func<CancellationToken, Task>? ValidateSource);
    private readonly ConditionalWeakTable<ModelTurn, ConsumerAdmission> consumerAdmissions = new();

    public Task WaitForQuiescenceAsync(CancellationToken token)
    {
        lock (dispatchGate)
        {
            return (quiescence?.Task ?? Task.CompletedTask).WaitAsync(token);
        }
    }

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

    private bool ConsumerAdmissionCurrent(ModelTurn turn) =>
        !consumerAdmissions.TryGetValue(turn, out var consumer) || consumer.Admitted();

    private async Task ValidateConsumerSourceAsync(ModelTurn turn, CancellationToken token)
    {
        if (consumerAdmissions.TryGetValue(turn, out var consumer) && consumer.ValidateSource is { } validate)
        { await validate(token).ConfigureAwait(false); }
    }
}
