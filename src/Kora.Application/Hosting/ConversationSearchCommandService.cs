using System.Diagnostics;

using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>Original-user, memory-only continuation over the canonical exact-conversation search.</summary>
public sealed class ConversationSearchCommandService(
    SessionWorkspaceService workspace, SessionConversationService conversation,
    ISessionWorkspaceAccess access, TimeProvider time)
{
    private readonly Lock gate = new();
    private Pending? pending;
    private Running? running;
    private long epoch;

    public void Retire()
    {
        lock (gate)
        {
            pending = null;
            epoch++;
            running?.Cancellation.Cancel();
            running = null;
        }
    }

    public async Task<ConversationSearchPage?> ExecuteAsync(SessionCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token)
    {
        SessionConversationService.RequireOriginalSource(origin);
        if (!command.IsConversationSearch) { throw new ArgumentException("An exact conversation search operation is required.", nameof(command)); }
        var session = new HostId<SessionIdentity>(command.SessionId
            ?? throw new ArgumentException("An exact session ID is required.", nameof(command)));
        session.Validate();
        var control = access.ControlRevision;
        bool SourceLive() => access.CanInspect && access.ControlRevision == control && admitted();
        token.ThrowIfCancellationRequested();
        if (!SourceLive()) { Retire(); throw new InvalidOperationException("Original private search admission changed."); }
        if (command.Operation == SessionCommandOperation.ConversationSearchCancel)
        {
            lock (gate)
            {
                if (pending?.Target.Authority.SessionId != session && running?.Session != session)
                { throw new InvalidOperationException("No host-held search belongs to that exact session."); }
                Retire();
            }
            return null;
        }
        Pending? previous;
        long attempt;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        lock (gate)
        {
            previous = pending;
            Retire();
            attempt = epoch;
            running = new(session, cancellation);
        }
        var cancellationToken = cancellation.Token;
        var next = command.Operation == SessionCommandOperation.ConversationSearchNext;
        bool Live()
        {
            lock (gate)
            {
                return epoch == attempt && SourceLive() && !cancellationToken.IsCancellationRequested
                    && (!next || previous is not null && previous.SourceAdmitted() && time.GetUtcNow() < previous.ExpiresAt);
            }
        }
        var links = previous is not null && next ? new[] { previous.Cause } : [];
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), origin),
            HostActivityLayer.Application, HostOperation.Request, links);
        try
        {
            if (next && (previous is null || previous.Target.Authority.SessionId != session
                || previous.Origin != origin || previous.ControlRevision != control
                || !previous.SourceAdmitted() || time.GetUtcNow() >= previous.ExpiresAt))
            { throw new InvalidOperationException("The original search source, cursor or expiry changed. Start a fresh search."); }
            var query = next ? previous!.Query : command.ConversationQuery
                ?? throw new ArgumentException("An explicit lexical query is required.", nameof(command));
            _ = new ConversationSearch(query);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Live()) { throw new InvalidOperationException("Search admission changed before reading."); }
            var target = await workspace.ReadExactMetadataAsync(session, cancellationToken).ConfigureAwait(false);
            if (!Live() || target.Authority.SessionId != session || next && target != previous!.Target)
            { throw new InvalidOperationException("The exact retained conversation source changed."); }
            var page = await conversation.SearchAsync(session, query, next ? previous!.Cursor : null,
                ConversationSearchPage.DefaultRecords, cancellationToken).ConfigureAwait(false);
            var current = await workspace.ReadExactMetadataAsync(session, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Live() || current != target || page.Session != target.Authority)
            { throw new InvalidOperationException("The exact conversation source or private admission changed before publication."); }
            lock (gate)
            {
                if (!Live()) { throw new InvalidOperationException("Search publication was revoked."); }
                if (page.Next is { } cursor)
                {
                    pending = new(target, query, cursor, origin, control,
                        next ? previous!.ExpiresAt : time.GetUtcNow().AddSeconds(60),
                        next ? previous!.SourceAdmitted : SourceLive, new(activity.Activity!.Context));
                }
            }
            activity.Complete(HostOperationOutcome.Completed);
            return page;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
        finally
        {
            lock (gate) { if (epoch == attempt) { running = null; } }
        }
    }

    private sealed record Running(HostId<SessionIdentity> Session, CancellationTokenSource Cancellation);
    private sealed record Pending(SessionWorkspaceEntry Target, string Query, ConversationSearchCursor Cursor,
        RequestOrigin Origin, long ControlRevision, DateTimeOffset ExpiresAt, Func<bool> SourceAdmitted, ActivityLink Cause);
}
