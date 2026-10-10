using System.Diagnostics;
using Kora.Core.Context;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Tools.Files;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

public sealed partial class SessionFileAttachmentService(
    SessionWorkspaceService sessions, ISessionWorkspaceAccess access, LocalSessionFileAttach action,
    ILocalFileRetrieval retrieval, TimeProvider time, ISecurityAuditLog audit,
    ILogger<SessionFileAttachmentService> logger) : IAsyncDisposable
{
    private PendingAttachment? pending;
    private PendingRemoval? pendingRemoval;
    private bool disposed;
    private long revision;
    private readonly Lock sync = new();
    private readonly HashSet<CancellationTokenSource> inspections = [];
    private readonly HashSet<HostId<SessionIdentity>> replacementControls = [];
    private TaskCompletionSource quiescence = CompletedQuiescence();
    private TaskCompletionSource controlQuiescence = CompletedQuiescence();
    private int controls;
    public LocalFileReview? Review => action.Review;
    public SessionFileReplacement? ReplacementReview => pending is { Previous: { } previous } captured
        && Review is { } next ? new(previous, captured.PreviousMetadata!, captured.PreviousCapturedAt, next) : null;
    public bool IsQuiescent { get { lock (sync) { return inspections.Count == 0 && controls == 0 && action.IsQuiescent; } } }
    public event Action<HostId<SessionIdentity>>? Revoked;
    public event Action? InspectionRevoked;

    private static void RequireNative() => SessionWorkspaceService.RequireNativeFileInput();

    public Task<LocalFileOutcome> Select(SessionWorkspaceEntry exactTarget, IUserFilePicker picker,
        Func<bool> admission, CancellationToken token) => SelectCore(exactTarget, null, picker, admission, token);

    public Task<LocalFileOutcome> SelectReplacement(SessionWorkspaceEntry exactTarget, SessionFileAttachment previous,
        IUserFilePicker picker, Func<bool> admission, CancellationToken token) =>
        SelectCore(exactTarget, previous, picker, admission, token);

    private async Task<LocalFileOutcome> SelectCore(SessionWorkspaceEntry exactTarget, SessionFileAttachment? previous,
        IUserFilePicker picker, Func<bool> admission, CancellationToken token)
    {
        RequireNative();
        if (disposed || !action.IsQuiescent || !access.CanControl || !exactTarget.Authority.IsActive || !admission())
        {
            throw new InvalidOperationException("Select an exact current active session under private host control.");
        }
        var control = access.ControlRevision;
        var current = Interlocked.Increment(ref revision);
        bool Eligible() => !disposed && Interlocked.Read(ref revision) == current && access.CanControl
            && access.ControlRevision == control && admission() && !token.IsCancellationRequested;
        var original = new HostRequest(new(Guid.NewGuid()), exactTarget.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        pending = new(exactTarget, original, Eligible);
        using var activity = HostActivity.BeginRoot(original, HostActivityLayer.Application, HostOperation.Request);
        var resolved = await sessions.ReadAttachmentTarget(exactTarget.Authority.SessionId, token).ConfigureAwait(false);
        if (resolved.Authority != exactTarget.Authority || !Eligible())
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw new InvalidOperationException("The exact selected active-session generation changed before native file review.");
        }
        var old = await Inspect(exactTarget.Authority.SessionId, "session.file.select-check",
            cancellation => sessions.ReadAttachment(exactTarget.Authority.SessionId, cancellation), token).ConfigureAwait(false);
        if (previous is null && old is not null)
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw new InvalidOperationException("Remove the exact existing attachment and Kora copies before another admission.");
        }
        if (previous is not null)
        {
            if (old is null || previous.Session != exactTarget.Authority.SessionId
                || previous.StorageRevision != old.StorageRevision || previous.File.Reference != old.File.Reference)
            {
                activity.Complete(HostOperationOutcome.Failed);
                throw new InvalidOperationException("The exact historical attachment changed before replacement. Inspect again.");
            }
            var inventory = await Inspect(previous.Session, "session.file.replace-review",
                cancellation => sessions.PreviewAttachmentRemoval(previous.Session, cancellation), token).ConfigureAwait(false);
            if (!inventory.BodyRetained || inventory.ReplacementCopyVerificationPending || inventory.ReplacementSwapUnconfirmed
                || inventory.StorageRevision != old.StorageRevision || inventory.File != old.File.Reference
                || inventory.Generation != exactTarget.Authority.Generation || !Eligible())
            {
                activity.Complete(HostOperationOutcome.Failed);
                throw new InvalidOperationException("The exact old attachment or copy inventory changed before replacement review.");
            }
            pending = new(exactTarget, original, Eligible, inventory, old.File.Review.Metadata, old.File.AdmittedAt);
            await RetireInspections(previous.Session).ConfigureAwait(false);
        }
        var result = await action.Select(picker, Eligible, token).ConfigureAwait(false);
        activity.Complete(result == LocalFileOutcome.Reviewed ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
        return result;
    }

    public async Task<LocalFileOutcome> Confirm(Guid exactReview, CancellationToken token)
    {
        RequireNative();
        var reviewed = Review;
        var captured = pending;
        if (reviewed is null || reviewed.ReviewId != exactReview || captured is null || !captured.Admitted())
        {
            throw new InvalidOperationException("This exact persistence review is stale. Select again; preview consent cannot persist.");
        }
        using var activity = HostActivity.BeginRoot(captured.Request, HostActivityLayer.Application, HostOperation.Request,
            [new ActivityLink(reviewed.Cause)]);
        var resolved = await sessions.ReadAttachmentTarget(captured.Request.SessionId, token).ConfigureAwait(false);
        if (resolved.Authority != captured.Target.Authority || !captured.Admitted())
        {
            activity.Complete(HostOperationOutcome.Failed);
            await Clear().ConfigureAwait(false);
            throw new InvalidOperationException("The exact session generation or current control changed before capture. Select again.");
        }
        using var control = new ControlScope(this);
        if (captured.Previous is not null) { lock (sync) { replacementControls.Add(captured.Request.SessionId); } }
        var auditEvent = captured.Previous is not null ? new SecurityAuditEvent(Guid.NewGuid(),
            SecurityAuditCategory.ProtectedOperation, "session.file.replace-copy", SecurityAuditOutcome.Requested,
            SecurityAuditInitiator.LocalUser, "session.file.local") : null;
        LocalFileOutcome outcome;
        try
        {
            if (auditEvent is not null)
            {
                audit.Write(auditEvent);
                await RetireInspections(captured.Request.SessionId).ConfigureAwait(false);
            }
            outcome = await action.Execute(exactReview, captured.Admitted, async (file, bytes, cancellation) =>
            {
                if (captured.Previous is { } old)
                {
                    await sessions.ReplaceAttachment(captured.Request, old, file, bytes, captured.Admitted, cancellation)
                        .ConfigureAwait(false);
                }
                else
                {
                    await sessions.CommitAttachment(captured.Request, captured.Target.Authority.Generation, file, bytes, captured.Admitted, cancellation)
                        .ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);
            if (auditEvent is not null)
            {
                audit.Write(auditEvent.WithOutcome(outcome == LocalFileOutcome.Admitted ? SecurityAuditOutcome.Succeeded
                    : outcome == LocalFileOutcome.Cancelled ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed,
                    outcome == LocalFileOutcome.Admitted ? "old-copies-verified" : "inspect-exact-durable-state"));
            }
        }
        catch (OperationCanceledException)
        {
            if (auditEvent is not null) { audit.Write(auditEvent.WithOutcome(SecurityAuditOutcome.Cancelled, "inspect-exact-durable-state")); }
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception)
        {
            if (auditEvent is not null) { audit.Write(auditEvent.WithOutcome(SecurityAuditOutcome.Failed, "inspect-exact-durable-state")); }
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
        finally
        {
            await Clear().ConfigureAwait(false);
            lock (sync) { replacementControls.Remove(captured.Request.SessionId); }
        }
        activity.Complete(outcome == LocalFileOutcome.Admitted ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
        return outcome;
    }

    private async Task RetireInspections(HostId<SessionIdentity> session)
    {
        Task outstanding;
        Task cancellation;
        lock (sync)
        {
            cancellation = Task.WhenAll(inspections.Select(inspection => inspection.CancelAsync()));
            outstanding = quiescence.Task;
        }
        Revoked?.Invoke(session);
        await cancellation.ConfigureAwait(false);
        await outstanding.ConfigureAwait(false);
    }

    public Task<SessionFileAttachment?> Read(HostId<SessionIdentity> session, CancellationToken token) =>
        Inspect(session, "session.file.read", async cancellation =>
    {
        RequireNative();
        lock (sync)
        {
            if (replacementControls.Contains(session))
            {
                throw new InvalidOperationException("Replacement is controlling this source. Explicitly inspect after completion or recovery.");
            }
        }
        var control = access.ControlRevision;
        var epoch = revision;
        var result = await sessions.ReadAttachment(session, cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        if (disposed || !access.CanInspect || control != access.ControlRevision || epoch != revision)
        {
            throw new InvalidOperationException("Snapshot disclosure was revoked during passive inspection.");
        }
        return result;
    }, token);

    public Task<LocalFileSearchResult> Search(SessionFileAttachment exact, string query, CancellationToken token) =>
        Inspect(exact.Session, "session.file.search", async cancellation =>
    {
        RequireNative();
        var epoch = revision;
        var current = await Read(exact.Session, cancellation).ConfigureAwait(false);
        if (current?.StorageRevision != exact.StorageRevision || current.File.Reference != exact.File.Reference)
        {
            throw new InvalidOperationException("This exact retained attachment is no longer available.");
        }
        var result = await Task.Run(() => retrieval.Search(current.File, current.File.Reference, query,
            time.GetUtcNow(), cancellation), cancellation).ConfigureAwait(false);
        var after = await Read(exact.Session, cancellation).ConfigureAwait(false);
        if (epoch != revision || after?.StorageRevision != exact.StorageRevision)
        {
            throw new InvalidOperationException("Attachment search was revoked before disclosure.");
        }
        return result;
    }, token);

    public async Task<SessionFileRemoval> PreviewRemoval(HostId<SessionIdentity> session, CancellationToken token)
    {
        var control = access.ControlRevision;
        var result = await Inspect(session, "session.file.remove-review",
            cancellation => sessions.PreviewAttachmentRemoval(session, cancellation), token).ConfigureAwait(false);
        lock (sync) { pendingRemoval = new(result, control, time.GetUtcNow().AddMinutes(2)); }
        return result;
    }

    public async Task Remove(SessionFileRemoval review, Func<bool> admitted, CancellationToken token)
    {
        RequireNative();
        PendingRemoval? reviewed;
        lock (sync) { reviewed = pendingRemoval; pendingRemoval = null; }
        if (reviewed is null || !ReferenceEquals(reviewed.Review, review)
            || reviewed.Control != access.ControlRevision || time.GetUtcNow() >= reviewed.ExpiresAt || !admitted())
        {
            throw new InvalidOperationException("Review this exact attachment and copy inventory under current private admission before removal.");
        }
        using var control = new ControlScope(this);
        bool CanRemove() => !disposed && access.ControlRevision == reviewed.Control && admitted();
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), review.Session,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var auditEvent = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "session.file.copy-cleanup", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "session.file.local");
        try
        {
            audit.Write(auditEvent);
            Interlocked.Increment(ref revision);
            Revoked?.Invoke(review.Session);
            await Clear().ConfigureAwait(false);
            await sessions.RemoveAttachment(review, CanRemove, token).ConfigureAwait(false);
            audit.Write(auditEvent.WithOutcome(SecurityAuditOutcome.Succeeded, "owned-copies-verified"));
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            audit.Write(auditEvent.WithOutcome(SecurityAuditOutcome.Cancelled, "inspect-revocation-and-inventory"));
            activity.Complete(HostOperationOutcome.Cancelled);
            Cancelled(logger);
            throw;
        }
        catch (Exception exception)
        {
            audit.Write(auditEvent.WithOutcome(SecurityAuditOutcome.Failed, "copy-removal-not-certified"));
            activity.Complete(HostOperationOutcome.Failed);
            Failure(logger, exception.GetType().Name);
            throw;
        }
    }

    public async Task Clear()
    {
        RevokeCore();
        await action.Clear().ConfigureAwait(false);
        await WaitForInspections().ConfigureAwait(false);
    }

    public void Revoke()
    {
        RevokeCore();
        InspectionRevoked?.Invoke();
    }

    private void RevokeCore()
    {
        lock (sync)
        {
            Interlocked.Increment(ref revision);
            foreach (var inspection in inspections.ToArray()) { inspection.Cancel(); }
        }
        pending = null;
        pendingRemoval = null;
        action.Revoke();
    }

    public async Task WaitForQuiescence()
    {
        Task outstanding;
        lock (sync) { outstanding = controlQuiescence.Task; }
        await WaitForInspections().ConfigureAwait(false);
        await outstanding.ConfigureAwait(false);
    }

    internal async Task RevokeForDisposition(HostId<SessionIdentity> session)
    {
        Revoked?.Invoke(session);
        await Clear().ConfigureAwait(false);
        await WaitForQuiescence().ConfigureAwait(false);
    }

    private async Task WaitForInspections()
    {
        Task outstanding;
        lock (sync) { outstanding = quiescence.Task; }
        await outstanding.ConfigureAwait(false);
        await action.WaitForQuiescence().ConfigureAwait(false);
    }

    private async Task<T> Inspect<T>(HostId<SessionIdentity> session, string actionId,
        Func<CancellationToken, Task<T>> operation, CancellationToken token)
    {
        RequireNative();
        var cause = HostActivity.Current?.Activity!.Context;
        var request = new HostRequest(new(Guid.NewGuid()), session, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request,
            cause is { } context ? [new ActivityLink(context)] : []);
        var auditEvent = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation, actionId,
            SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "session.file.local");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (inspections.Count == 0) { quiescence = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            inspections.Add(cancellation);
        }
        try
        {
            audit.Write(auditEvent);
            var result = await operation(cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            audit.Write(auditEvent.WithOutcome(SecurityAuditOutcome.Succeeded, "retained-historical-source"));
            cancellation.Token.ThrowIfCancellationRequested();
            activity.Complete(HostOperationOutcome.Completed);
            return result;
        }
        catch (OperationCanceledException)
        {
            audit.Write(auditEvent.WithOutcome(SecurityAuditOutcome.Cancelled, "revoked"));
            activity.Complete(HostOperationOutcome.Cancelled);
            Cancelled(logger);
            throw;
        }
        catch (Exception exception)
        {
            audit.Write(auditEvent.WithOutcome(SecurityAuditOutcome.Failed, "unavailable"));
            activity.Complete(HostOperationOutcome.Failed);
            Revoked?.Invoke(session);
            Failure(logger, exception.GetType().Name);
            throw;
        }
        finally
        {
            lock (sync)
            {
                inspections.Remove(cancellation);
                if (inspections.Count == 0) { quiescence.TrySetResult(); }
            }
        }
    }

    private static TaskCompletionSource CompletedQuiescence()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completed.SetResult();
        return completed;
    }
    public async ValueTask DisposeAsync()
    {
        disposed = true;
        Revoke();
        await Clear().ConfigureAwait(false);
        await WaitForQuiescence().ConfigureAwait(false);
        await action.DisposeAsync().ConfigureAwait(false);
    }

    private sealed record PendingAttachment(SessionWorkspaceEntry Target, HostRequest Request, Func<bool> Admitted,
        SessionFileRemoval? Previous = null, LocalFileMetadata? PreviousMetadata = null, DateTimeOffset PreviousCapturedAt = default);
    private sealed record PendingRemoval(SessionFileRemoval Review, long Control, DateTimeOffset ExpiresAt);

    private sealed class ControlScope : IDisposable
    {
        private readonly SessionFileAttachmentService owner;
        internal ControlScope(SessionFileAttachmentService owner)
        {
            this.owner = owner;
            lock (owner.sync)
            {
                if (owner.controls++ == 0) { owner.controlQuiescence = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            }
        }
        public void Dispose()
        {
            lock (owner.sync)
            {
                if (--owner.controls == 0) { owner.controlQuiescence.TrySetResult(); }
            }
        }
    }
}
