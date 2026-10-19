using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

/// <summary>Executes only this host's admitted single-use turn, retaining the consumer's current-source fence.</summary>
public interface IModelTurnExecution
{
    Task<ModelTurnResult> RunAsync(ModelTurn turn, Func<bool> admitted, CancellationToken token,
        Func<CancellationToken, Task>? validateSource = null);
}
