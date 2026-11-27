using Kora.Core.Providers;

namespace Kora.Core.Dependencies;

/// <summary>Measures both SDK-generated input bodies locally, without provider transmission or answer generation.</summary>
public interface IClaudeHistoryInputPreparation
{
    /// <summary>Returns the exact combined Count Tokens and Messages UTF-8 input charge for the captured profile.</summary>
    Task<int> MeasureHistoryInputAsync(ModelProviderSelection selection, ProviderProfileTuple profile,
        ModelContextEnvelope context, CancellationToken token);
}
