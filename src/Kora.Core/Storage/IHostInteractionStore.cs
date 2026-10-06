using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.Storage;

/// <summary>
/// An admitted store serializes the transition with host-resolved intent, session lifecycle,
/// policy and operation/content changes, and atomically persists its records and typed audit.
/// Intent is the matching committed nonterminal task record; a root intent may omit invocation ID,
/// but a supplied invocation ID must match. Terminal/cancelled work cannot admit new decisions.
/// Questions are session-owned; Perpetual grants are independently retained even after session deletion.
/// The snapshot is a request-local view: persist changed records only, never evict records outside it.
/// Include all affected grants for content observations. Done/delete/resume must advance session generation;
/// Active-session restart preserves it. Content observation/revocation must precede publishing changed content.
/// No callback changes become visible on failure. OperationCanceledException certifies rollback;
/// uncertain commits must throw a storage error, never a success receipt or cancellation.
/// </summary>
public interface IHostInteractionStore
{
    ValueTask<HostInteractionDecision> TransactAsync(
        HostRequest request,
        Func<HostInteractionSnapshot, HostInteractionCommit> transition,
        CancellationToken cancellationToken);
}
