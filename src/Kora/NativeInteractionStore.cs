using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora;

// The live desktop gate can only narrow durable authority, including inside the serialized transition.
internal sealed class NativeInteractionStore(IHostInteractionStore store, Func<bool> canInteract) : IHostInteractionStore
{
    public ValueTask<HostInteractionDecision> TransactAsync(HostRequest request,
        Func<HostInteractionSnapshot, HostInteractionCommit> transition, CancellationToken cancellationToken) =>
        store.TransactAsync(request, snapshot =>
        {
            var gated = canInteract() ? snapshot : snapshot with
            {
                Policy = snapshot.Policy with { IsUnlocked = false, OtherMandatoryGatesSatisfied = false },
            };
            var commit = transition(gated);
            return commit with { Snapshot = commit.Snapshot with { Policy = snapshot.Policy } };
        }, cancellationToken);
}
