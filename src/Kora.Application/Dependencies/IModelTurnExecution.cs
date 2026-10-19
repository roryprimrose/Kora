using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

/// <summary>Executes only this host's admitted single-use turn, retaining the consumer's current-source fence.</summary>
public interface IModelTurnExecution
{
    /// <summary>Waits for actual adapter termination, not merely cancellation of presentation authority.</summary>
    Task WaitForQuiescenceAsync(CancellationToken token);

    Task<ModelTurnResult> RunAsync(ModelTurn turn, Func<bool> admitted, CancellationToken token,
        Func<CancellationToken, Task>? validateSource = null);
}
