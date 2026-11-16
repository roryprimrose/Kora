using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.Dependencies;

/// <summary>Native-only, volatile evaluation admission. No release evidence, installation, inference or persisted authority.</summary>
public sealed partial class WindowsOllamaEvaluationSource(
    IWindowsOllamaEvaluationObservation observation, TimeProvider time, ISecurityAuditLog audit,
    ILogger<WindowsOllamaEvaluationSource> logger) : IWindowsOllamaQualificationSource, IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private WindowsOllamaEvaluationReview? candidate;
    private WindowsOllamaEvaluationReview? verified;
    private WindowsOllamaQualification? receipt;
    private CancellationTokenSource? revoked;
    private WindowsOllamaAdmissionSubject? admittedSubject;
    private DateTimeOffset verifiedUntil;
    private Task? disposal;
    private long revision;
    private bool disposed;

    public bool HasAdmission { get { lock (gate) { return receipt is not null; } } }
    public bool IsQuiescent => operations.CurrentCount == 1;

    public async Task<WindowsOllamaEvaluationReview> ObserveAsync(Func<bool> admitted, CancellationToken token)
    {
        var request = RequireNative(admitted);
        await operations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            long expected;
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                RevokeCore();
                expected = revision;
            }
            using var deadline = Deadline(token);
            var subject = await observation.ObserveEvaluationAsync(deadline.Token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The exact supported running Ollama runtime and pinned model are unavailable.");
            deadline.Token.ThrowIfCancellationRequested();
            RequireNative(admitted, request);
            lock (gate)
            {
                if (disposed || expected != revision)
                { throw new InvalidOperationException("Evaluation observation was revoked before review."); }
                return candidate = new(subject, time.GetUtcNow().AddMinutes(5));
            }
        }
        catch (Exception exception)
        {
            EvaluationFailed(logger, exception);
            throw;
        }
        finally { operations.Release(); }
    }

    public async Task<WindowsOllamaEvaluationReview> RevalidateAsync(
        WindowsOllamaEvaluationReview reviewed, Func<bool> admitted, CancellationToken token)
    {
        var request = RequireNative(admitted);
        await operations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            lock (gate) { RequireReview(reviewed); verified = null; }
            using var deadline = Deadline(token);
            if (!await observation.IsEvaluationCurrentAsync(reviewed.Subject, deadline.Token).ConfigureAwait(false))
            { throw new InvalidOperationException("The exact reviewed runtime or pinned model changed."); }
            deadline.Token.ThrowIfCancellationRequested();
            RequireNative(admitted, request);
            lock (gate)
            {
                RequireReview(reviewed);
                verified = reviewed;
                verifiedUntil = time.GetUtcNow().AddSeconds(30);
                return reviewed;
            }
        }
        catch (Exception exception)
        {
            DiscardPendingReview(reviewed);
            EvaluationFailed(logger, exception);
            throw;
        }
        finally { operations.Release(); }
    }

    public WindowsOllamaQualification Admit(WindowsOllamaEvaluationReview reviewed, Func<bool> admitted, Func<bool> remainsEligible,
        CancellationToken token)
    {
        var request = RequireNative(admitted);
        var issuer = HostActivity.RequireCurrent();
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
            "model.ollama.evaluation", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
            issuer.Request.TaskId.Value.ToString("D"));
        lock (gate)
        {
            try
            {
                audit.Write(requested);
                token.ThrowIfCancellationRequested();
                RequireReview(reviewed);
                if (!ReferenceEquals(verified, reviewed) || verifiedUntil <= time.GetUtcNow() || !remainsEligible())
                { throw new InvalidOperationException("Evaluation requires current native confirmation of the revalidated review."); }
                var now = time.GetUtcNow();
                revoked = new();
                var selection = new ModelProviderSelection(ModelProviderIdentity.Ollama, new(Guid.NewGuid()), new(revision));
                var model = new ProviderCatalogueModel(new(WindowsOllamaSetupService.Model),
                    WindowsOllamaSetupService.Model + " (live evaluation; not production-qualified)", selection,
                    [new(new("off"), "Off", false)], [new(new("bounded"), "Host-bounded", true)]);
                var catalogue = new ProviderCapabilityCatalogue(ProviderService.Local, ProviderCatalogueStatus.Current,
                    reviewed.Subject.Account, new(revision), now, now.AddHours(1), [model]);
                var prepared = WindowsOllamaQualification.ForEvaluation(Guid.NewGuid(), catalogue,
                    reviewed.Subject.Runtime, remainsEligible, revoked.Token);
                RequireNative(admitted, request);
                token.ThrowIfCancellationRequested();
                if (!remainsEligible()) { throw new InvalidOperationException("Private evaluation authority changed."); }
                audit.Write(requested.WithOutcome(SecurityAuditOutcome.Succeeded));
                RequireReview(reviewed);
                RequireNative(admitted, request);
                token.ThrowIfCancellationRequested();
                if (!remainsEligible()) { throw new InvalidOperationException("Private evaluation authority changed during audit commit."); }
                receipt = prepared;
                admittedSubject = reviewed.Subject;
                candidate = verified = null;
                return prepared;
            }
            catch (Exception exception)
            {
                DiscardPendingReview(reviewed);
                EvaluationFailed(logger, exception);
                audit.Write(requested.WithOutcome(token.IsCancellationRequested
                    ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Denied));
                throw;
            }
        }
    }

    public async Task<WindowsOllamaQualification?> ReadAsync(CancellationToken cancellationToken)
    {
        await operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WindowsOllamaQualification expected;
            WindowsOllamaAdmissionSubject subject;
            lock (gate)
            {
                if (disposed || receipt is null) { return null; }
                if (!receipt.IsCurrent(time.GetUtcNow()))
                {
                    RevokeCore();
                    EvaluationUnavailable(logger, "expired-or-private-authority-changed");
                    return null;
                }
                expected = receipt;
                subject = admittedSubject ?? throw new InvalidOperationException("The original evaluation subject is unavailable.");
            }
            using var deadline = Deadline(cancellationToken, expected.Revoked);
            var current = await observation.IsEvaluationCurrentAsync(subject, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            lock (gate)
            {
                if (!ReferenceEquals(receipt, expected)) { return null; }
                if (!current || !expected.IsCurrent(time.GetUtcNow()))
                {
                    RevokeCore();
                    EvaluationUnavailable(logger, "runtime-model-or-private-authority-changed");
                    return null;
                }
                return expected;
            }
        }
        catch (Exception exception)
        {
            Revoke();
            EvaluationFailed(logger, exception);
            throw;
        }
        finally { operations.Release(); }
    }

    private CancellationTokenSource Deadline(CancellationToken token, CancellationToken revokedToken = default)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token, revokedToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        return deadline;
    }

    private void RequireReview(WindowsOllamaEvaluationReview reviewed)
    {
        if (disposed || !ReferenceEquals(candidate, reviewed) || reviewed.ExpiresAt <= time.GetUtcNow())
        { throw new InvalidOperationException("The exact evaluation review expired or was revoked."); }
    }

    private static HostRequest RequireNative(Func<bool> admitted, HostRequest? expected = null)
    {
        var issuer = HostActivity.RequireCurrent();
        if (issuer.Request.Origin != RequestOrigin.LocalUi || issuer.Request.InvocationId is not null
            || issuer.Activity!.IsStopped || issuer.Outcome != HostOperationOutcome.Unknown
            || expected is not null && !ReferenceEquals(issuer.Request, expected) || !admitted())
        { throw new InvalidOperationException("Ollama evaluation requires current original private native input."); }
        return issuer.Request;
    }

    public void DiscardPendingReview() { lock (gate) { if (receipt is null) { RevokeCore(); } } }
    public void DiscardPendingReview(WindowsOllamaEvaluationReview? reviewed)
    {
        lock (gate) { if (reviewed is not null && ReferenceEquals(candidate, reviewed)) { RevokeCore(); } }
    }
    public void Revoke() { lock (gate) { RevokeCore(); } }
    public void Revoke(WindowsOllamaQualification expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        lock (gate) { if (ReferenceEquals(receipt, expected)) { RevokeCore(); } }
    }

    public void RevokeNative(Func<bool> admitted)
    {
        var request = RequireNative(admitted);
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
            "model.ollama.evaluation.revoke", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
            request.TaskId.Value.ToString("D"));
        lock (gate)
        {
            try
            {
                RevokeCore();
                audit.Write(requested);
                audit.Write(requested.WithOutcome(SecurityAuditOutcome.Succeeded));
            }
            catch (Exception exception)
            {
                EvaluationFailed(logger, exception);
                throw;
            }
        }
    }

    private void RevokeCore()
    {
        revision++;
        candidate = verified = null;
        receipt = null;
        admittedSubject = null;
        var prior = revoked;
        revoked = null;
        try { prior?.Cancel(); }
        finally { prior?.Dispose(); }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposal is not null) { return new(disposal); }
            disposed = true;
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            disposal = completed.Task;
            // Publish the owned completion before revocation callbacks can re-enter disposal.
            _ = DisposeCoreAsync(completed);
            return new(disposal);
        }
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completed)
    {
        try
        {
            try { lock (gate) { RevokeCore(); } }
            finally
            {
                try { await lifetime.CancelAsync().ConfigureAwait(false); }
                finally
                {
                    await operations.WaitAsync().ConfigureAwait(false);
                    operations.Release();
                    lifetime.Dispose();
                }
            }
            completed.SetResult();
        }
        catch (Exception exception) { completed.SetException(exception); }
    }
}
