using Kora.Core.Providers;

namespace Kora.Core.Dependencies;

/// <summary>Optional, separately qualified native Claude text-only streaming operation.</summary>
public interface IClaudeAnswerTurnRuntime : IClaudeTurnRuntime
{
    /// <summary>Owns both reviewed POSTs and the physical reader until joined; returns only a validated final answer.</summary>
    /// <param name="provenance">Exact original host turn.</param>
    /// <param name="profile">Exact approved account/model/profile.</param>
    /// <param name="context">Frozen tool-free original-user context.</param>
    /// <param name="admit">Separate final-byte consent for each physical POST.</param>
    /// <param name="current">Fresh host authority, including source and privacy.</param>
    /// <param name="publish">Awaited provisional decoded text, never retention authority.</param>
    /// <param name="token">Owned request lifetime, including reviews.</param>
    /// <returns>The complete validated terminal answer or a non-success without an answer.</returns>
    Task<ClaudeTurnReply> RunWithAnswerAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
        ModelContextEnvelope context, Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
        Func<CancellationToken, ValueTask<bool>> current,
        Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish, CancellationToken token);
}
