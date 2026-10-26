namespace Kora.Core.Dependencies;

/// <summary>A single owned Claude transport lease for the canonical host continuation loop.</summary>
public interface IClaudeToolTurnRuntime : IClaudeTurnRuntime
{
    /// <summary>Opens an account-bound transport, without sending content or granting tool authority.</summary>
    /// <param name="provenance">The original host turn.</param>
    /// <param name="profile">The exact admitted account and catalogue profile.</param>
    /// <param name="context">The frozen original request and observation subset.</param>
    /// <param name="admit">Independent affirmative review for each physical content POST.</param>
    /// <param name="current">Live host authority, checked across asynchronous work.</param>
    /// <param name="token">Cancellation of the entire owned turn.</param>
    /// <returns>An owned transport, or <c>null</c> when the account/profile is unavailable.</returns>
    Task<IModelToolTransport?> OpenToolTurnAsync(ModelTurnProvenance provenance,
        Providers.ProviderProfileTuple profile, ModelContextEnvelope context,
        Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
        Func<CancellationToken, ValueTask<bool>> current, CancellationToken token);
}
