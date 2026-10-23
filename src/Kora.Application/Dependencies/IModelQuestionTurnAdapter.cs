using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Application.Dependencies;

/// <summary>Opt-in structured clarification inside the same single-use, tool-free admitted turn.</summary>
internal interface IModelQuestionTurnAdapter
{
    /// <summary>Waits for at most one host-validated answer using the owning execution token, never a new cancellation source.</summary>
    /// <param name="provenance">The original host-issued turn and request identity.</param>
    /// <param name="profile">The exact admitted provider profile, if any.</param>
    /// <param name="context">The complete immutable tool-free input envelope.</param>
    /// <param name="invokeTool">The host tool-denial callback, never an alternate permission path.</param>
    /// <param name="validateContinuation">The current source, session, profile and ownership fence.</param>
    /// <param name="ask">The single exact host-owned question continuation.</param>
    /// <param name="cancellationToken">The existing owning turn's cancellation token.</param>
    /// <returns>A genuine final response or explicit terminal failure; never a question disguised as a final answer.</returns>
    Task<ModelProviderReply> RunWithQuestionAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation,
        Func<ModelClarificationQuestion, CancellationToken, Task<ModelQuestionAnswerMessage>> ask,
        CancellationToken cancellationToken);
}
