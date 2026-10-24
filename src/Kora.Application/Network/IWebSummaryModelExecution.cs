using Kora.Core.Dependencies;

namespace Kora.Application.Network;

/// <summary>Admits a fresh local host turn for one exact reviewed source, without tools or hosted fallback.</summary>
public interface IWebSummaryModelExecution
{
    /// <summary>Executes only while the exact source and original-user admission remain current.</summary>
    Task<ModelTurnResult> Run(WebSummaryReview review, Func<bool> current, CancellationToken token);
}
