using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Application.Dependencies;

/// <summary>Optional bounded tool-free provisional presentation on an explicitly admitted host turn.</summary>
internal interface IModelAnswerTurnAdapter
{
    /// <summary>Runs one owned answer with fresh admission before each provisional publication.</summary>
    /// <param name="provenance">Exact host-issued turn.</param>
    /// <param name="profile">Admitted provider profile.</param>
    /// <param name="context">Complete frozen host context.</param>
    /// <param name="invokeTool">Existing host denial path; streaming does not admit tools.</param>
    /// <param name="validateContinuation">Fresh host admission.</param>
    /// <param name="publish">Awaited volatile answer publication.</param>
    /// <param name="cancellationToken">Owned turn lifetime.</param>
    /// <returns>A complete terminal result independent of provisional snapshots.</returns>
    Task<ModelProviderReply> RunWithAnswerAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation,
        Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish, CancellationToken cancellationToken);
}
