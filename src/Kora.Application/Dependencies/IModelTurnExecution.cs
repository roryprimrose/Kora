using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

/// <summary>Executes only this host's admitted single-use turn, retaining the consumer's current-source fence.</summary>
public interface IModelTurnExecution
{
    /// <summary>Consumes one turn issued by the same host under its still-live original request activity.</summary>
    /// <param name="turn">The single-use admitted turn; never reconstruct it from provider correlation.</param>
    /// <param name="admitted">The consumer's current privacy, source and ownership fence, retained for late callbacks.</param>
    /// <param name="token">Cancellation closes publication and tool authority, not proof of resource quiescence.</param>
    /// <param name="validateSource">Optional asynchronous exact-source validation around continuation boundaries.</param>
    /// <returns>The host-correlated terminal outcome; only success can contain an answer.</returns>
    Task<ModelTurnResult> RunAsync(ModelTurn turn, Func<bool> admitted, CancellationToken token,
        Func<CancellationToken, Task>? validateSource = null);
}
