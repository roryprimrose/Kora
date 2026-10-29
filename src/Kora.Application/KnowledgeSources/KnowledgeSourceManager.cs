using System.Diagnostics;
using System.Security.Cryptography;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;

namespace Kora.Application.KnowledgeSources;

public sealed class KnowledgeSourceManager(
    IKnowledgeSourceInspector inspector, IKnowledgeSourceStore store, ISecurityAuditLog audit, TimeProvider time) : IDisposable
{
    private IKnowledgeSourceSelection? selection;
    private KnowledgeSource? expected;
    private DateTimeOffset expires;
    private HostRequest? original;
    private ActivityContext cause;
    private string name = string.Empty;
    private bool disposed;
    private int busy;
    private readonly List<byte[]> capturedBytes = [];
    public KnowledgeSourceReview? Review { get; private set; }
    public KnowledgeSourceRevision? Candidate { get; private set; }

    public Task<KnowledgeSourceReview> ReviewAsync(string displayName, string selectedPath,
        KnowledgeSource? refresh, Func<bool> eligible, CancellationToken cancellationToken) =>
        ReviewAsync(displayName, selectedPath, refresh, refresh?.Folder.Scope ?? KnowledgeSourceScope.ImmediateFiles,
            eligible, cancellationToken);

    public async Task<KnowledgeSourceReview> ReviewAsync(string displayName, string selectedPath,
        KnowledgeSource? refresh, KnowledgeSourceScope scope, Func<bool> eligible, CancellationToken cancellationToken)
    {
        Enter(eligible);
        using var deadline = new CancellationTokenSource(KnowledgeSourcePolicy.MaximumOperationTime, time);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        cancellationToken = operation.Token;
        try
        {
            Clear();
            KnowledgeSourcePolicy.ValidateScope(scope);
            if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 80
                || displayName.Any(char.IsControl))
            { throw new InvalidDataException("Use a nonempty source name of at most 80 characters without control characters."); }
            name = displayName.Trim();
            var host = HostActivity.RequireCurrent();
            original = host.Request;
            cause = host.Activity!.Context;
            if (refresh is not null)
            {
                if (!string.Equals(selectedPath, refresh.Folder.CanonicalPath, StringComparison.OrdinalIgnoreCase)
                    || scope != refresh.Folder.Scope)
                { throw new InvalidDataException("Refresh cannot change the registered root or traversal policy; remove and add with a full fresh review."); }
                // Once the exact native refresh is admitted, join revocation even if later work is cancelled.
                expected = await Audited("refresh", () => store.BeginRefreshAsync(refresh, CancellationToken.None)).ConfigureAwait(false);
                if (expected.Folder.Scope != scope
                    || !string.Equals(expected.Folder.CanonicalPath, refresh.Folder.CanonicalPath, StringComparison.Ordinal)
                    || !string.Equals(expected.Folder.DirectoryIdentity, refresh.Folder.DirectoryIdentity, StringComparison.Ordinal))
                { throw new InvalidDataException("Refresh admission disagrees with the exact registered root and policy."); }
            }
            Check(eligible, cancellationToken);
            selection = await inspector.InspectSourceAsync(selectedPath, scope, cancellationToken).ConfigureAwait(false);
            Check(eligible, cancellationToken);
            if (selection.Metadata.Scope != scope
                || !string.Equals(selection.Metadata.CanonicalPath, selectedPath, StringComparison.OrdinalIgnoreCase))
            { throw new InvalidDataException("Discovery did not return the exact selected root and policy."); }
            if (refresh is not null && !string.Equals(selection.Metadata.DirectoryIdentity,
                refresh.Folder.DirectoryIdentity, StringComparison.Ordinal))
            { throw new InvalidDataException("The registered physical root changed; remove it and explicitly add a new source."); }
            expires = time.GetUtcNow().AddMinutes(2);
            Review = new(Guid.NewGuid(), refresh?.Id ?? Guid.NewGuid(), original, selection.Metadata, cause);
            return Review;
        }
        catch { Clear(); throw; }
        finally { Volatile.Write(ref busy, 0); }
    }

    public async Task<KnowledgeSourceRevision> CaptureAsync(Guid reviewId, Func<bool> eligible, CancellationToken cancellationToken)
    {
        Enter(eligible);
        using var deadline = new CancellationTokenSource(KnowledgeSourcePolicy.MaximumOperationTime, time);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        cancellationToken = operation.Token;
        try
        {
            var review = Review;
            CheckReview(reviewId, eligible, cancellationToken);
            Review = null; // Native read consent is one-shot, including failed capture.
            return await Audited("capture", async () =>
            {
                await selection!.ValidateAsync(cancellationToken).ConfigureAwait(false);
                var files = new List<LocalFileRevision>();
                foreach (var item in review!.Metadata.Files)
                {
                    Check(eligible, cancellationToken);
                    var bytes = await selection.ReadAsync(item, cancellationToken).ConfigureAwait(false);
                    capturedBytes.Add(bytes);
                    files.Add(new(new(review.ReviewId, review.SourceId, review.Request, item, cause), bytes, time.GetUtcNow()));
                }
                await selection.ValidateAsync(cancellationToken).ConfigureAwait(false);
                Check(eligible, cancellationToken);
                Candidate = new(review, files);
                expires = time.GetUtcNow().AddMinutes(2);
                return Candidate;
            }).ConfigureAwait(false);
        }
        catch { Clear(); throw; }
        finally { Volatile.Write(ref busy, 0); }
    }

    public async Task<KnowledgeSource> PublishAsync(KnowledgeSourceRevision exactCandidate, Func<bool> eligible,
        CancellationToken cancellationToken)
    {
        Enter(eligible);
        using var deadline = new CancellationTokenSource(KnowledgeSourcePolicy.MaximumOperationTime, time);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        cancellationToken = operation.Token;
        KnowledgeSource result;
        KnowledgeSource? published = null;
        try
        {
            Check(eligible, cancellationToken);
            if (!ReferenceEquals(Candidate, exactCandidate) || time.GetUtcNow() >= expires)
            { throw new InvalidOperationException("The exact snapshot confirmation is stale."); }
            Candidate = null;
            await selection!.ValidateAsync(cancellationToken).ConfigureAwait(false);
            Check(eligible, cancellationToken);
            result = await Audited("publish", async () =>
            {
                published = await store.PublishAsync(expected, name, exactCandidate,
                    capturedBytes.Select(bytes => (ReadOnlyMemory<byte>)bytes).ToArray(), cancellationToken).ConfigureAwait(false);
                Check(eligible, cancellationToken);
                return published;
            }).ConfigureAwait(false);
        }
        catch
        {
            if (published is not null)
            { await store.DisableAsync(published, CancellationToken.None).ConfigureAwait(false); }
            throw;
        }
        finally { Clear(); Volatile.Write(ref busy, 0); }
        return result;
    }

    public async Task<IReadOnlyList<KnowledgeSource>> ListAsync(Func<bool> eligible, CancellationToken cancellationToken)
    {
        Enter(eligible);
        try
        {
            var sources = await store.ListAsync(cancellationToken).ConfigureAwait(false);
            Check(eligible, cancellationToken);
            return sources;
        }
        finally { Volatile.Write(ref busy, 0); }
    }

    public async Task<KnowledgeSourceRevision> OpenAsync(KnowledgeSource exact, Func<bool> eligible, CancellationToken cancellationToken)
    {
        Enter(eligible);
        using var deadline = new CancellationTokenSource(KnowledgeSourcePolicy.MaximumOperationTime, time);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        cancellationToken = operation.Token;
        try
        {
            return await Audited("inspect", async () =>
            {
                using var access = await inspector.InspectSourceAsync(exact.Folder.CanonicalPath, exact.Folder.Scope,
                    cancellationToken).ConfigureAwait(false);
                if (!access.Metadata.Matches(exact.Folder))
                { throw new InvalidDataException("The original source membership or access changed; explicitly refresh before reuse."); }
                var snapshot = await store.ReadAsync(exact, cancellationToken).ConfigureAwait(false);
                Check(eligible, cancellationToken);
                if (snapshot.Review.SourceId != exact.Id || !snapshot.Review.Metadata.Matches(exact.Folder))
                { throw new InvalidDataException("The retained snapshot does not match the exact registered source."); }
                await access.ValidateAsync(cancellationToken).ConfigureAwait(false);
                Check(eligible, cancellationToken);
                return snapshot;
            }).ConfigureAwait(false);
        }
        finally { Volatile.Write(ref busy, 0); }
    }

    public async Task DisableAsync(KnowledgeSource exact, Func<bool> eligible, CancellationToken cancellationToken)
    {
        Enter(eligible);
        try
        {
            await Audited("disable", async () =>
            {
                await store.DisableAsync(exact, cancellationToken).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        }
        finally { Volatile.Write(ref busy, 0); }
    }

    public async Task RemoveAsync(KnowledgeSource exact, DateTimeOffset reviewedAt, Func<bool> eligible,
        CancellationToken cancellationToken)
    {
        Enter(eligible);
        try
        {
            var now = time.GetUtcNow();
            if (reviewedAt > now || now - reviewedAt >= TimeSpan.FromMinutes(2))
            { throw new InvalidOperationException("Removal inventory review expired; inspect the current inventory again."); }
            await Audited("remove", async () =>
            {
                await store.RemoveAsync(exact, cancellationToken).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        }
        finally { Volatile.Write(ref busy, 0); }
    }

    private async Task<T> Audited<T>(string operation, Func<Task<T>> action)
    {
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Storage);
        var entry = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "knowledge." + operation, SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "knowledge.device-local");
        audit.Write(entry);
        try
        {
            var result = await action().ConfigureAwait(false);
            audit.Write(entry.WithOutcome(SecurityAuditOutcome.Succeeded));
            activity.Complete(HostOperationOutcome.Completed);
            return result;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            audit.Write(entry.WithOutcome(SecurityAuditOutcome.Cancelled));
            throw;
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            audit.Write(entry.WithOutcome(SecurityAuditOutcome.Failed));
            throw;
        }
    }

    private void Enter(Func<bool> eligible)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Check(eligible, CancellationToken.None);
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
        { throw new InvalidOperationException("A knowledge source operation is still owned; wait for it to finish."); }
    }

    private static void Check(Func<bool> eligible, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var host = HostActivity.RequireCurrent();
        if (!eligible() || host.Request.Origin != RequestOrigin.LocalUi || host.Request.InvocationId is not null
            || host.Outcome != HostOperationOutcome.Unknown)
        { throw new InvalidOperationException("Original private native source admission is unavailable."); }
    }

    private void CheckReview(Guid id, Func<bool> eligible, CancellationToken cancellationToken)
    {
        Check(eligible, cancellationToken);
        if (Review?.ReviewId != id || time.GetUtcNow() >= expires)
        { throw new InvalidOperationException("The exact native read review is stale."); }
    }

    private void Clear()
    {
        selection?.Dispose();
        selection = null;
        expected = null;
        Review = null;
        Candidate = null;
        original = null;
        foreach (var bytes in capturedBytes) { CryptographicOperations.ZeroMemory(bytes); }
        capturedBytes.Clear();
    }

    public void Dispose()
    {
        if (Volatile.Read(ref busy) != 0)
        { throw new InvalidOperationException("Join the owned source operation before disposal."); }
        disposed = true;
        Clear();
    }
}
