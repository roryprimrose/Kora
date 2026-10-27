using System.Diagnostics;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    internal async Task PresentNamedConversationAsync(SessionWorkspaceEntry target, Func<bool> admitted,
        RequestOrigin origin, CancellationToken token)
    {
        if (!CanRead || conversationBusy || conversation is null)
        { throw new InvalidOperationException("The existing conversation reader is unavailable or busy."); }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
        token = cancellation.Token;
        var control = access.ControlRevision;
        var epoch = selectionEpoch;
        bool Live() => !closed && access.CanInspect && control == access.ControlRevision
            && epoch == selectionEpoch && admitted();
        async Task Validate()
        {
            token.ThrowIfCancellationRequested();
            if (!Live()) { throw new OperationCanceledException("Original exact conversation choice changed."); }
            var exact = await service.SearchListAsync(SessionListSearchKind.ExactId, SessionListFilter.All,
                target.Authority.SessionId.Value.ToString("D"), null, 1, token);
            if (!Live() || exact.Records.Length != 1 || exact.Records[0] != target)
            { throw new OperationCanceledException("The acknowledged conversation metadata or lifecycle changed."); }
        }
        ActivityLink[] causes = HostActivity.Current?.Activity is { } cause ? [new(cause.Context)] : [];
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), target.Authority.SessionId,
            new(Guid.NewGuid()), origin), HostActivityLayer.Desktop, HostOperation.Presentation, causes);
        busy = true;
        Notify();
        try
        {
            await Validate();
            var page = await conversation.ReadPageAsync(target.Authority.SessionId, null, 25, token);
            await Validate();
            if (page.Session != target.Authority)
            { throw new InvalidDataException("Conversation history belongs to another source generation."); }
            if (selected == target && conversationPage is { } retained
                && retained.HeadRevision == page.HeadRevision && retained.Ceiling == page.Ceiling)
            {
                activity.Complete(HostOperationOutcome.Completed);
                return;
            }
            ClearSelection();
            selected = target;
            nameDraft = target.Metadata!.Name.Value;
            historySessionId = target.Authority.SessionId.Value.ToString("D");
            sessionFilter = SessionListFilter.All;
            sessions = new([target], null);
            conversationPage = page;
            composerAuthority = target.Authority;
            conversationStatus = "Opened the acknowledged exact conversation and its real retained history. Nothing was submitted.";
            status = conversationStatus;
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
        finally { busy = false; Notify(); if (closed) { lifetime.Dispose(); } }
    }
}
