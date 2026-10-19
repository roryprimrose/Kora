using Kora.Core.Dependencies;

namespace Kora.Core.Context;

/// <summary>Executes an admitted single-file context through the existing local-only model host.</summary>
public interface ILocalFileAnswerModel
{
    /// <summary>Rechecks source admission around host admission and execution; never selects a hosted destination.</summary>
    Task<LocalContextTurnReceipt> RunAsync(ModelContextEnvelope context, Func<bool> isCurrent, CancellationToken cancellationToken);
}
