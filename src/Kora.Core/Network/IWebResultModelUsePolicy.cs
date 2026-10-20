using Kora.Core.Presentation;

namespace Kora.Core.Network;

/// <summary>Source-specific sensitivity and local-model use admission, independent of retrieval consent.</summary>
public interface IWebResultModelUsePolicy
{
    /// <summary>Evaluates the exact complete immutable source; absence or unknown policy never admits context construction.</summary>
    Task<WebResultModelUseDecision> AdmitAsync(WebResultSnapshot snapshot, CancellationToken token);
}
