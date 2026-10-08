using System.Security.Cryptography;
using System.Text;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging;

namespace Kora.Tools.Files;

public sealed partial class LocalFilePreview(
    ILocalFileInspector inspector, ISecurityAuditLog audit, TimeProvider time,
    ILogger<LocalFilePreview> logger) : IDisposable, IAsyncDisposable
{
    private readonly Lock sync = new();
    private ILocalFileSelection? selection;
    private LocalFileReview? review;
    private LocalFileRevision? revision;
    private Func<bool>? eligible;
    private CancellationTokenSource? pending;
    private TaskCompletionSource? quiescence;
    private long generation;
    private bool disposed;
    private bool releaseFailed;
    private DateTimeOffset reviewDeadline;

    public event EventHandler? Changed;
    public bool IsBusy { get { lock (sync) { return pending is not null; } } }
    public bool IsQuiescent { get { lock (sync) { return pending is null && selection is null && !releaseFailed; } } }
    public LocalFileReview? Review { get { Revalidate(); lock (sync) { return review; } } }
    public LocalFileRevision? Current { get { Revalidate(); lock (sync) { return pending is null ? revision : null; } } }

    private void Revalidate()
    {
        bool close;
        lock (sync) { close = eligible is not null && (!eligible() || (review is not null && time.GetUtcNow() >= reviewDeadline)); }
        if (close) { Clear(); }
    }

    public Task<LocalFileOutcome> SelectAsync(IUserFilePicker picker, Func<bool> canPresent, CancellationToken token) =>
        RunAsync("file.preview.select", canPresent, async (request, admittedGeneration, cancellation) =>
        {
            var path = await picker.SelectAsync(cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (path is null) { return LocalFileOutcome.Cancelled; }
            lock (sync)
            {
                if (!IsCurrent(admittedGeneration, canPresent, cancellation)) { return LocalFileOutcome.Cancelled; }
            }
            LocalFilePolicy.ValidatePath(path);
            var candidate = await inspector.InspectAsync(path, cancellation).ConfigureAwait(false);
            try
            {
                var metadata = candidate.Metadata;
                LocalFilePolicy.ValidatePath(metadata.CanonicalPath);
                if (!string.Equals(path, metadata.CanonicalPath, StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(metadata.FileIdentity) || metadata.ByteLength < 0)
                {
                    throw new InvalidDataException("The selected canonical source identity could not be verified.");
                }
                if (metadata.ByteLength > LocalFilePolicy.MaximumBytes) { return LocalFileOutcome.Oversize; }
                lock (sync)
                {
                    if (!IsCurrent(admittedGeneration, canPresent, cancellation)) { return LocalFileOutcome.Cancelled; }
                    selection = candidate;
                    review = new(Guid.NewGuid(), Guid.NewGuid(), request, metadata, HostActivity.RequireCurrent().Activity!.Context);
                    reviewDeadline = time.GetUtcNow().AddMinutes(2);
                    candidate = null;
                    return LocalFileOutcome.Reviewed;
                }
            }
            finally { ReleaseOwned(candidate); }
        }, preserveReview: false, token);

    public Task<LocalFileOutcome> ConfirmAsync(Guid exactReviewId, Func<bool> canPresent, CancellationToken token)
    {
        // Confirmation is native UI only. Typed/spoken IDs or document instructions do not authorize a read.
        var request = HostActivity.RequireCurrent().Request;
        Revalidate();
        lock (sync)
        {
            if (request.Origin != RequestOrigin.LocalUi || review is null || review.ReviewId != exactReviewId
                || request.SessionId != review.Request.SessionId || request.TaskId != review.Request.TaskId
                || eligible is null || !eligible())
            {
                return Task.FromResult(LocalFileOutcome.Stale);
            }
        }
        return RunAsync("file.preview.admit", canPresent, async (_, admittedGeneration, cancellation) =>
        {
            ILocalFileSelection selected;
            LocalFileReview reviewed;
            lock (sync)
            {
                if (review?.ReviewId != exactReviewId || selection is null) { return LocalFileOutcome.Stale; }
                reviewed = review;
                selected = selection;
                selection = null;
            }
            byte[]? bytes = null;
            LocalFileRevision captured;
            try
            {
                bytes = await selected.ReadAsync(cancellation).ConfigureAwait(false);
                captured = new LocalFileRevision(reviewed, bytes, time.GetUtcNow());
            }
            finally
            {
                if (bytes is not null) { CryptographicOperations.ZeroMemory(bytes); }
                ReleaseOwned(selected);
            }
            lock (sync)
            {
                if (releaseFailed) { return LocalFileOutcome.Unavailable; }
                if (!IsCurrent(admittedGeneration, canPresent, cancellation) || eligible is null || !eligible())
                {
                    return LocalFileOutcome.Cancelled;
                }
                revision = captured;
                review = null;
                return LocalFileOutcome.Admitted;
            }
        }, preserveReview: true, token);
    }

    private bool IsCurrent(long admittedGeneration, Func<bool> gate, CancellationToken token) =>
        !disposed && generation == admittedGeneration && !token.IsCancellationRequested && gate();

    private async Task<LocalFileOutcome> RunAsync(string action, Func<bool> gate,
        Func<HostRequest, long, CancellationToken, Task<LocalFileOutcome>> operation, bool preserveReview, CancellationToken token)
    {
        var request = HostActivity.RequireCurrent().Request;
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        CancellationTokenSource cancellation;
        TaskCompletionSource done;
        long admittedGeneration;
        lock (sync)
        {
            if (disposed || !ClipboardCommand.IsDeliberateOrigin(request.Origin) || !gate() || releaseFailed)
            {
                activity.Complete(HostOperationOutcome.Failed);
                return LocalFileOutcome.Denied;
            }
            if (pending is not null)
            {
                activity.Complete(HostOperationOutcome.Failed);
                return LocalFileOutcome.Busy;
            }
            if (!preserveReview)
            {
                ReleaseSelection();
                if (releaseFailed)
                {
                    review = null;
                    revision = null;
                    eligible = null;
                    activity.Complete(HostOperationOutcome.Failed);
                    return LocalFileOutcome.Unavailable;
                }
                review = null;
                revision = null;
                eligible = gate;
                ++generation;
            }
            admittedGeneration = generation;
            pending = cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            quiescence = done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        var auditEvent = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation, action,
            SecurityAuditOutcome.Requested, request.Origin == RequestOrigin.ActivatedVoice
                ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser, "file.preview.local");
        var outcome = LocalFileOutcome.Unavailable;
        try
        {
            audit.Write(auditEvent);
            cancellation.Token.ThrowIfCancellationRequested();
            outcome = await operation(request, admittedGeneration, cancellation.Token).ConfigureAwait(false);
            lock (sync) { if (releaseFailed) { outcome = LocalFileOutcome.Unavailable; } }
            audit.Write(auditEvent.WithOutcome(outcome is LocalFileOutcome.Admitted or LocalFileOutcome.Reviewed
                ? SecurityAuditOutcome.Succeeded : outcome == LocalFileOutcome.Cancelled
                    ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed, outcome.ToString().ToLowerInvariant()));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            outcome = LocalFileOutcome.Cancelled;
            audit.Write(auditEvent.WithOutcome(SecurityAuditOutcome.Cancelled, "cancelled"));
        }
        catch (DecoderFallbackException)
        {
            outcome = LocalFileOutcome.InvalidText;
            audit.Write(auditEvent.WithOutcome(SecurityAuditOutcome.Failed, "invalid-utf8"));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            outcome = LocalFileOutcome.Unavailable;
            Failure(logger, exception.GetType().Name);
            audit.Write(auditEvent.WithOutcome(SecurityAuditOutcome.Failed, "source-unavailable"));
        }
        finally
        {
            lock (sync)
            {
                if (outcome is not (LocalFileOutcome.Reviewed or LocalFileOutcome.Admitted))
                {
                    ReleaseSelection();
                    review = null;
                    revision = null;
                }
                pending = null;
            }
            cancellation.Dispose();
            done.SetResult();
        }
        Report(logger, outcome);
        activity.Complete(outcome is LocalFileOutcome.Reviewed or LocalFileOutcome.Admitted ? HostOperationOutcome.Completed
            : outcome == LocalFileOutcome.Cancelled ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        Changed?.Invoke(this, EventArgs.Empty);
        return outcome;
    }

    private void ReleaseSelection()
    {
        var releasing = selection;
        selection = null;
        ReleaseOwned(releasing);
    }

    private void ReleaseOwned(ILocalFileSelection? releasing)
    {
        try { releasing?.Dispose(); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            lock (sync) { releaseFailed = true; }
            Failure(logger, exception.GetType().Name);
        }
    }

    public void Clear()
    {
        lock (sync)
        {
            var subject = review ?? revision?.Review;
            using var activity = subject is not null
                ? HostActivity.BeginRoot(subject.Request, HostActivityLayer.Application, HostOperation.Recovery, [new(subject.Cause)])
                : null;
            ++generation;
            pending?.Cancel();
            ReleaseSelection();
            review = null;
            revision = null;
            eligible = null;
            activity?.Complete(releaseFailed ? HostOperationOutcome.Failed : HostOperationOutcome.Completed);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task WaitForQuiescenceAsync()
    {
        Task outstanding;
        lock (sync) { outstanding = quiescence?.Task ?? Task.CompletedTask; }
        await outstanding.ConfigureAwait(false);
        lock (sync)
        {
            if (selection is not null || releaseFailed)
            {
                throw new InvalidOperationException("File preview handles were not released; clean ownership handoff is blocked.");
            }
        }
    }

    public void Dispose()
    {
        lock (sync) { disposed = true; }
        Clear();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await WaitForQuiescenceAsync().ConfigureAwait(false);
    }
}
