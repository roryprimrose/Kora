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
    private ILocalFolderSelection? folderSelection;
    private LocalFolderReview? folderReview;
    private LocalFolderRevision? folderRevision;
    private Func<bool>? eligible;
    private CancellationTokenSource? pending;
    private TaskCompletionSource? quiescence;
    private long generation;
    private bool disposed;
    private bool releaseFailed;
    private bool retrieving;
    private DateTimeOffset reviewDeadline;

    public event EventHandler? Changed;
    public bool IsBusy { get { lock (sync) { return pending is not null; } } }
    public bool IsQuiescent { get { lock (sync) { return pending is null && selection is null && folderSelection is null && !releaseFailed; } } }
    public LocalFileReview? Review { get { Revalidate(); lock (sync) { return pending is null ? review : null; } } }
    public LocalFileRevision? Current { get { Revalidate(); lock (sync) { return pending is null || retrieving ? revision : null; } } }
    public LocalFolderReview? FolderReview { get { Revalidate(); lock (sync) { return pending is null ? folderReview : null; } } }
    public LocalFolderRevision? CurrentFolder { get { Revalidate(); lock (sync) { return pending is null || retrieving ? folderRevision : null; } } }

    internal Task<LocalFileSearchResult> SearchAsync(LocalFileReference exactSource, Func<bool> gate,
        DateTimeOffset observedAt, Func<LocalFileRevision, CancellationToken, Task<LocalFileSearchResult>> search,
        Action<LocalFileSearchResult> commit, CancellationToken token) =>
        SearchAsync(() => revision?.Reference == exactSource ? revision : null, item => item.Review.Request,
            gate, observedAt, search, commit, token);

    internal Task<LocalFileSearchResult> SearchAsync(LocalFolderReference exactSource, Func<bool> gate,
        DateTimeOffset observedAt, Func<LocalFolderRevision, CancellationToken, Task<LocalFileSearchResult>> search,
        Action<LocalFileSearchResult> commit, CancellationToken token) =>
        SearchAsync(() => folderRevision?.Reference == exactSource ? folderRevision : null, item => item.Review.Request,
            gate, observedAt, search, commit, token);

    private async Task<LocalFileSearchResult> SearchAsync<TRevision>(Func<TRevision?> resolve,
        Func<TRevision, HostRequest> originalRequest, Func<bool> gate,
        DateTimeOffset observedAt, Func<TRevision, CancellationToken, Task<LocalFileSearchResult>> search,
        Action<LocalFileSearchResult> commit, CancellationToken token) where TRevision : class
    {
        var request = HostActivity.RequireCurrent().Request;
        Revalidate();
        CancellationTokenSource cancellation;
        TaskCompletionSource done;
        TRevision admitted;
        long admittedGeneration;
        lock (sync)
        {
            var candidate = resolve();
            LocalFileSearchOutcome? denied = disposed || releaseFailed ? LocalFileSearchOutcome.Unavailable
                : !ClipboardCommand.IsDeliberateOrigin(request.Origin) || !gate() ? LocalFileSearchOutcome.Denied
                : pending is not null ? LocalFileSearchOutcome.Busy
                : candidate is null || eligible is null || !eligible()
                    ? LocalFileSearchOutcome.Stale
                : request.SessionId != originalRequest(candidate).SessionId || request.TaskId != originalRequest(candidate).TaskId
                    ? LocalFileSearchOutcome.Denied : null;
            if (denied is { } outcome)
            {
                var refused = LocalFileSearchResult.Empty(outcome, observedAt);
                commit(refused);
                return refused;
            }
            admitted = candidate!;
            admittedGeneration = generation;
            retrieving = true;
            pending = cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            quiescence = done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try
        {
            LocalFileSearchResult result;
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                result = await search(admitted, cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                result = LocalFileSearchResult.Empty(LocalFileSearchOutcome.Cancelled, observedAt);
            }
            lock (sync)
            {
                if (!IsCurrent(admittedGeneration, gate, cancellation.Token) || !ReferenceEquals(resolve(), admitted)
                    || eligible is null || !eligible())
                {
                    result = LocalFileSearchResult.Empty(disposed ? LocalFileSearchOutcome.Unavailable
                        : token.IsCancellationRequested ? LocalFileSearchOutcome.Cancelled : LocalFileSearchOutcome.Stale, observedAt);
                }
                // Audit publication and exact-generation revalidation share the revocation boundary.
                commit(result);
                return result;
            }
        }
        finally
        {
            lock (sync) { retrieving = false; pending = null; }
            cancellation.Dispose();
            done.SetResult();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Revalidate()
    {
        bool close;
        lock (sync) { close = eligible is not null && (!eligible() || ((review is not null || folderReview is not null) && time.GetUtcNow() >= reviewDeadline)); }
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
            return await ReviewFileAsync(path, request, admittedGeneration, canPresent, null, cancellation).ConfigureAwait(false);
        }, preserveReview: false, token);

    private async Task<LocalFileOutcome> ReviewFileAsync(string path, HostRequest request, long admittedGeneration,
        Func<bool> canPresent, LocalFileReview? previous, CancellationToken cancellation)
    {
            LocalFilePolicy.ValidatePath(path);
            var candidate = await inspector.InspectAsync(path, cancellation).ConfigureAwait(false);
            try
            {
                var metadata = candidate.Metadata;
                LocalFilePolicy.ValidatePath(metadata.CanonicalPath);
                if (!string.Equals(path, metadata.CanonicalPath, StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(metadata.FileIdentity) || metadata.ByteLength < 0
                    || (previous is not null && !string.Equals(metadata.FileIdentity, previous.Metadata.FileIdentity, StringComparison.Ordinal)))
                {
                    throw new InvalidDataException("The selected canonical source identity could not be verified.");
                }
                if (metadata.ByteLength > LocalFilePolicy.MaximumBytes) { return LocalFileOutcome.Oversize; }
                lock (sync)
                {
                    if (!IsCurrent(admittedGeneration, canPresent, cancellation)) { return LocalFileOutcome.Cancelled; }
                    selection = candidate;
                    review = new(Guid.NewGuid(), previous?.SourceId ?? Guid.NewGuid(), previous?.Request ?? request,
                        metadata, HostActivity.RequireCurrent().Activity!.Context) { PreviousMetadata = previous?.Metadata };
                    reviewDeadline = time.GetUtcNow().AddMinutes(2);
                    candidate = null;
                    return LocalFileOutcome.Reviewed;
                }
            }
            finally { ReleaseOwned(candidate); }
    }

    public Task<LocalFileOutcome> SelectFolderAsync(IUserFolderPicker picker, Func<bool> canPresent, CancellationToken token) =>
        RunAsync("folder.preview.select", canPresent, async (request, admittedGeneration, cancellation) =>
        {
            var path = await picker.SelectFolderAsync(cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (path is null) { return LocalFileOutcome.Cancelled; }
            lock (sync)
            {
                if (!IsCurrent(admittedGeneration, canPresent, cancellation)) { return LocalFileOutcome.Cancelled; }
            }
            return await ReviewFolderAsync(path, request, admittedGeneration, canPresent, null, cancellation).ConfigureAwait(false);
        }, preserveReview: false, token);

    private async Task<LocalFileOutcome> ReviewFolderAsync(string path, HostRequest request, long admittedGeneration,
        Func<bool> canPresent, LocalFolderReview? previous, CancellationToken cancellation)
    {
            LocalFilePolicy.ValidateFolderPath(path);
            var candidate = await inspector.InspectFolderAsync(path, cancellation).ConfigureAwait(false);
            try
            {
                var metadata = candidate.Metadata;
                _ = LocalFolderPolicy.Validate(metadata.CanonicalPath, metadata.DirectoryIdentity, metadata.Files);
                if (!string.Equals(path, metadata.CanonicalPath, StringComparison.OrdinalIgnoreCase)
                    || (previous is not null && !string.Equals(metadata.DirectoryIdentity, previous.Metadata.DirectoryIdentity, StringComparison.Ordinal)))
                {
                    throw new InvalidDataException("The exact canonical folder identity could not be verified.");
                }
                lock (sync)
                {
                    if (!IsCurrent(admittedGeneration, canPresent, cancellation)) { return LocalFileOutcome.Cancelled; }
                    folderSelection = candidate;
                    folderReview = new(Guid.NewGuid(), previous?.SourceId ?? Guid.NewGuid(), previous?.Request ?? request,
                        metadata, HostActivity.RequireCurrent().Activity!.Context) { PreviousMetadata = previous?.Metadata };
                    reviewDeadline = time.GetUtcNow().AddMinutes(2);
                    candidate = null;
                    return LocalFileOutcome.Reviewed;
                }
            }
            finally { ReleaseOwned(candidate); }
    }

    internal Task<LocalFileOutcome> RefreshAsync(LocalFileReference exactSource, Func<bool> gate, CancellationToken token)
    {
        Revalidate();
        LocalFileRevision? original;
        Func<bool>? originalGate;
        lock (sync) { original = revision; originalGate = eligible; }
        bool CanPresent() => gate() && originalGate!();
        return RunAsync("file.preview.refresh", gate, (request, admittedGeneration, cancellation) =>
            ReviewFileAsync(original!.Review.Metadata.CanonicalPath, request, admittedGeneration, CanPresent,
                original.Review, cancellation), preserveReview: false, token,
            () => original is not null && ReferenceEquals(revision, original) && original.Reference == exactSource
                && IsRefreshContext(original.Review.Request), CanPresent);
    }

    internal Task<LocalFileOutcome> RefreshAsync(LocalFolderReference exactSource, Func<bool> gate, CancellationToken token)
    {
        Revalidate();
        LocalFolderRevision? original;
        Func<bool>? originalGate;
        lock (sync) { original = folderRevision; originalGate = eligible; }
        bool CanPresent() => gate() && originalGate!();
        return RunAsync("folder.preview.refresh", gate, (request, admittedGeneration, cancellation) =>
            ReviewFolderAsync(original!.Review.Metadata.CanonicalPath, request, admittedGeneration, CanPresent,
                original.Review, cancellation), preserveReview: false, token,
            () => original is not null && ReferenceEquals(folderRevision, original) && original.Reference == exactSource
                && IsRefreshContext(original.Review.Request), CanPresent);
    }

    private bool IsRefreshContext(HostRequest original)
    {
        var request = HostActivity.RequireCurrent().Request;
        // Called under the revocation lock only after resolving the still-admitted exact revision.
        return eligible!() && request.SessionId == original.SessionId && request.TaskId == original.TaskId;
    }

    public Task<LocalFileOutcome> ConfirmFolderAsync(Guid exactReviewId, Func<bool> canPresent, CancellationToken token)
    {
        var request = HostActivity.RequireCurrent().Request;
        Revalidate();
        lock (sync)
        {
            if (request.Origin != RequestOrigin.LocalUi || folderReview is null || folderReview.ReviewId != exactReviewId
                || request.SessionId != folderReview.Request.SessionId || request.TaskId != folderReview.Request.TaskId
                || eligible is null || !eligible())
            {
                return Task.FromResult(LocalFileOutcome.Stale);
            }
        }
        return RunAsync("folder.preview.admit", canPresent, async (_, admittedGeneration, cancellation) =>
        {
            ILocalFolderSelection selected;
            LocalFolderReview reviewed;
            lock (sync)
            {
                if (folderReview?.ReviewId != exactReviewId || folderSelection is null) { return LocalFileOutcome.Stale; }
                reviewed = folderReview;
                selected = folderSelection;
                folderSelection = null;
            }
            LocalFolderRevision captured;
            try
            {
                await selected.ValidateAsync(cancellation).ConfigureAwait(false);
                var files = new List<LocalFileRevision>();
                foreach (var metadata in reviewed.Metadata.Files)
                {
                    cancellation.ThrowIfCancellationRequested();
                    byte[]? bytes = null;
                    try
                    {
                        bytes = await selected.ReadAsync(metadata, cancellation).ConfigureAwait(false);
                        files.Add(new(new(reviewed.ReviewId, reviewed.SourceId, reviewed.Request, metadata, reviewed.Cause),
                            bytes, time.GetUtcNow()));
                    }
                    finally
                    {
                        if (bytes is not null) { CryptographicOperations.ZeroMemory(bytes); }
                    }
                }
                await selected.ValidateAsync(cancellation).ConfigureAwait(false);
                captured = new(reviewed, files);
            }
            finally { ReleaseOwned(selected); }
            lock (sync)
            {
                if (releaseFailed) { return LocalFileOutcome.Unavailable; }
                if (!IsCurrent(admittedGeneration, canPresent, cancellation) || eligible is null || !eligible())
                {
                    return LocalFileOutcome.Cancelled;
                }
                folderRevision = captured;
                folderReview = null;
                return LocalFileOutcome.Admitted;
            }
        }, preserveReview: true, token);
    }

    public Task<LocalFileOutcome> ConfirmAsync(Guid exactReviewId, Func<bool> canPresent, CancellationToken token) =>
        ConfirmAsync(exactReviewId, canPresent, null, token);

    public Task<LocalFileOutcome> ConfirmAttachment(Guid exactReviewId, Func<bool> canPresent,
        Func<LocalFileRevision, ReadOnlyMemory<byte>, CancellationToken, Task> persist, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(persist);
        return ConfirmAsync(exactReviewId, canPresent, persist, token);
    }

    private Task<LocalFileOutcome> ConfirmAsync(Guid exactReviewId, Func<bool> canPresent,
        Func<LocalFileRevision, ReadOnlyMemory<byte>, CancellationToken, Task>? persist, CancellationToken token)
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
                ReleaseOwned(selected);
                selected = null!;
                lock (sync)
                {
                    if (releaseFailed || !IsCurrent(admittedGeneration, canPresent, cancellation)
                        || eligible is null || !eligible()) { return LocalFileOutcome.Cancelled; }
                }
                if (persist is not null)
                {
                    await persist(captured, bytes, cancellation).ConfigureAwait(false);
                }
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
        Func<HostRequest, long, CancellationToken, Task<LocalFileOutcome>> operation, bool preserveReview, CancellationToken token,
        Func<bool>? exactSource = null, Func<bool>? retainedGate = null)
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
            if (exactSource is not null && !exactSource())
            {
                activity.Complete(HostOperationOutcome.Failed);
                return LocalFileOutcome.Stale;
            }
            if (!preserveReview)
            {
                ReleaseSelection();
                if (releaseFailed)
                {
                    DiscardContent();
                    eligible = null;
                    activity.Complete(HostOperationOutcome.Failed);
                    return LocalFileOutcome.Unavailable;
                }
                DiscardContent();
                eligible = retainedGate ?? gate;
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
            Changed?.Invoke(this, EventArgs.Empty);
            audit.Write(auditEvent);
            cancellation.Token.ThrowIfCancellationRequested();
            var candidateOutcome = await operation(request, admittedGeneration, cancellation.Token).ConfigureAwait(false);
            lock (sync)
            {
                if (releaseFailed) { candidateOutcome = LocalFileOutcome.Unavailable; }
                else if (!IsCurrent(admittedGeneration, gate, cancellation.Token) || eligible is null || !eligible())
                {
                    candidateOutcome = LocalFileOutcome.Cancelled;
                }
                audit.Write(auditEvent.WithOutcome(candidateOutcome is LocalFileOutcome.Admitted or LocalFileOutcome.Reviewed
                    ? SecurityAuditOutcome.Succeeded : candidateOutcome == LocalFileOutcome.Cancelled
                        ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed, candidateOutcome.ToString().ToLowerInvariant()));
                outcome = IsCurrent(admittedGeneration, gate, cancellation.Token) && eligible is not null && eligible()
                    ? candidateOutcome : LocalFileOutcome.Cancelled;
                if (outcome == LocalFileOutcome.Cancelled && candidateOutcome is LocalFileOutcome.Reviewed or LocalFileOutcome.Admitted)
                {
                    audit.Write(auditEvent.WithOutcome(SecurityAuditOutcome.Cancelled, "revoked-before-publication"));
                }
            }
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
                    DiscardContent();
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
        var releasingFolder = folderSelection;
        folderSelection = null;
        ReleaseOwned(releasingFolder);
    }

    private void DiscardContent()
    {
        review = null;
        revision = null;
        folderReview = null;
        folderRevision = null;
    }

    private void ReleaseOwned(IDisposable? releasing)
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
            var folderSubject = folderReview ?? folderRevision?.Review;
            var request = subject?.Request ?? folderSubject?.Request;
            var cause = subject?.Cause ?? folderSubject?.Cause;
            using var activity = request is not null && cause is not null
                ? HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Recovery, [new(cause.Value)])
                : null;
            ++generation;
            pending?.Cancel();
            ReleaseSelection();
            DiscardContent();
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
            if (selection is not null || folderSelection is not null || releaseFailed)
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
