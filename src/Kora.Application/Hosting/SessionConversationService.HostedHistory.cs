using System.Diagnostics;
using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    private Task<ConversationSubmissionReceipt> SubmitHostedHistoryAsync(SessionWorkspaceEntry target, string body,
        Func<bool> admitted, Func<CancellationToken, Task>? beforeRetain, IEnumerable<ActivityLink>? links,
        RequestOrigin origin, ConversationProviderRoute route, PendingConversation? pending, CancellationToken token)
    {
        HostedConversationHistoryContext? history = null;
        ClaudeHistoryInputBudget? claudeBudget = null;
        var control = access.ControlRevision;
        bool Live() => access.CanInspect && access.CanControl && access.ControlRevision == control && admitted();

        async ValueTask<bool> Current(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!Live() || history is null) { return false; }
            if (await store.ReadMetadataAsync(target.Authority.SessionId, cancellation).ConfigureAwait(false) != target)
            { return false; }
            if (history.Selections.Count != 0)
            {
                await workspace.ValidateConversationHistoryAsync(target, history.Selections, Live, cancellation)
                    .ConfigureAwait(false);
            }
            return Live() && (claudeBudget is null || await claudeBudget.Current(cancellation).ConfigureAwait(false));
        }

        async Task<ModelContextEnvelope> Prepare(HostRequest request, Func<bool> current, CancellationToken cancellation)
        {
            var now = time.GetUtcNow();
            history = new(request, body, now, now.Add(HostedTurnAuthorization.MaximumDuration));
            async Task Include(ConversationHistorySelection selection)
            {
                var candidate = history.Copy();
                if (!candidate.Include(selection)) { return; }
                if (route == ConversationProviderRoute.Claude)
                {
                    if (turns is not ModelTurnHost host)
                    { throw new InvalidOperationException("The Settings-routed Claude history host is unavailable."); }
                    claudeBudget ??= await host.PrepareClaudeHistoryInputBudgetAsync(history.Context, cancellation)
                        .ConfigureAwait(false);
                    if (await claudeBudget.Measure(candidate.Context, cancellation).ConfigureAwait(false)
                        > ModelContextEnvelope.MaximumInputUtf8Bytes) { return; }
                }
                history = candidate;
            }
            void RequireCurrent()
            {
                cancellation.ThrowIfCancellationRequested();
                if (!Live() || !current()) { throw new InvalidOperationException("Hosted history private admission changed."); }
            }
            RequireCurrent();
            var snapshot = await conversations.ReadConversationPageAsync(request.SessionId, null, 1, cancellation)
                .ConfigureAwait(false);
            RequireCurrent();
            if (snapshot.Session != target.Authority || snapshot.Ceiling is < 0 or > ConversationText.MaximumSessionMessages)
            { throw new InvalidOperationException("The hosted history session changed; no other conversation was substituted."); }
            for (var sequence = snapshot.Ceiling; sequence > 0
                && history.Selections.Count < ModelContextEnvelope.MaximumEvidenceItems; sequence--)
            {
                RequireCurrent();
                var cursor = new ConversationCursor(request.SessionId, snapshot.Session.Generation,
                    snapshot.Ceiling, sequence - 1, snapshot.HeadRevision);
                var page = await conversations.ReadConversationPageAsync(request.SessionId, cursor, 1, cancellation)
                    .ConfigureAwait(false);
                RequireCurrent();
                if (page.Session != snapshot.Session || page.Ceiling != snapshot.Ceiling
                    || page.HeadRevision != snapshot.HeadRevision || page.Messages.Length != 1
                    || page.Messages[0].Sequence != sequence)
                { throw new InvalidDataException("Hosted history requires the unchanged complete exact-session snapshot."); }
                var message = page.Messages[0];
                if (message.Request.RequestId == request.RequestId) { continue; }
                await Include(new(message, ConversationHistoryRole.Assistant)).ConfigureAwait(false);
                await Include(new(message, ConversationHistoryRole.User)).ConfigureAwait(false);
            }
            if (!await Current(cancellation).ConfigureAwait(false))
            { throw new InvalidOperationException("Hosted history sources changed before admission."); }
            return history.Context;
        }

        return SubmitCoreAsync(target, body, Live, beforeRetain, links, origin,
            (request, now) => new HostedConversationHistoryContext(request, body, now,
                now.Add(HostedTurnAuthorization.MaximumDuration)).Context,
            ModelTurnChoice.Hosted, token, copilot: route == ConversationProviderRoute.Copilot,
            claude: route == ConversationProviderRoute.Claude, pending: pending, ordinaryActiveDeadline: true,
            prepareContext: Prepare, hostedSourceCurrent: Current);
    }
}
