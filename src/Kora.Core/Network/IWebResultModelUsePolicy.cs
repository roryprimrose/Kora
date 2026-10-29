using Kora.Core.Presentation;

namespace Kora.Core.Network;

/// <summary>Private LocalOnly source-risk admission, independent of retrieval consent and non-sensitivity claims.</summary>
public interface IWebResultModelUsePolicy
{
    /// <summary>Evaluates the exact complete immutable source; absence or unknown policy never admits context construction.</summary>
    Task<WebResultModelUseDecision> AdmitAsync(WebResultSnapshot snapshot, CancellationToken token);
}
