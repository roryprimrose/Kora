using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Application.Configuration;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Dependencies;

/// <summary>Host-owned producer of volatile qualified process-use receipts. Persisted choices never restore live authority.</summary>
internal sealed partial class OllamaAdmissionWorkflow(
    ILocalModelQualificationAuthority authority, ILocalModelAdmissionBroker broker,
    LocalModelAdmissionPreferences preferences, AudioControlAdmission admission, ISecurityAuditLog audit,
    TimeProvider time, ILogger<OllamaAdmissionWorkflow> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Lock disposalGate = new();
    private Task? disposal;
    private bool disposed;
    private OllamaAdmissionReview? issued;
    internal string Status { get; private set; } = "Review local model admission. Saved choices do not authorize a running process after restart.";

    internal async Task<OllamaAdmissionReview?> Review(RequestOrigin origin, Func<bool> eligible, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            issued = null;
            return await admission.RunObservedAsync(origin, eligible, async cancellation =>
            {
                _ = preferences.Read();
                var trusted = await authority.Read(cancellation).ConfigureAwait(false);
                var current = trusted.Where(item => item.IsCurrent(time.GetUtcNow())).ToArray();
                if (current.Length != 1)
                { return (Evidence: (LocalModelQualificationEvidence?)null, Subject: (LocalModelAdmissionCandidate?)null); }
                using var deadline = new CancellationTokenSource(ModelToolProtocol.Deadline, time);
                using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellation, deadline.Token);
                var observed = await broker.Observe(current[0], bounded.Token).ConfigureAwait(false);
                return (Evidence: (LocalModelQualificationEvidence?)current[0], Subject: observed);
            }, (request, session, result) =>
            {
                if (result.Evidence is null || result.Subject is null || !result.Evidence.IsCurrent(time.GetUtcNow()))
                {
                    RevokeCurrent();
                    Status = result.Evidence is null
                        ? "Unavailable: this build has no single current release-qualified profile. A reviewed release must include exact qualification evidence; consent cannot qualify a model."
                        : "Unavailable: start the exact approved runtime yourself, verify its local model and account/session, then review again. Kora will not install or start it.";
                    Unavailable(logger, "qualification-or-current-subject-missing");
                    return null;
                }
                var expires = time.GetUtcNow().AddSeconds(30);
                if (result.Evidence.ExpiresAt < expires) { expires = result.Evidence.ExpiresAt; }
                issued = new(session, request.Origin, result.Evidence, result.Subject, expires, eligible);
                Status = issued.Description;
                return issued;
            }, linked.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Hold(exception);
            throw;
        }
        finally { gate.Release(); }
    }

    internal async Task Confirm(OllamaAdmissionReview review, RequestOrigin origin, Func<bool> eligible, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            RequireReview(review, origin, eligible);
            await admission.RunObservedAsync(origin, () => eligible() && review.Eligible(), async cancellation =>
            {
                var trusted = await authority.Read(cancellation).ConfigureAwait(false);
                if (!trusted.Any(item => string.Equals(item.Digest, review.Evidence.Digest, StringComparison.Ordinal)
                    && item.IsCurrent(time.GetUtcNow())))
                { throw new InvalidDataException("Reviewed qualification was removed, replaced or expired; review the current release again."); }
                using var deadline = new CancellationTokenSource(ModelToolProtocol.Deadline, time);
                using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellation, deadline.Token);
                return await broker.IsCurrent(review.Evidence, review.Subject, bounded.Token).ConfigureAwait(false);
            }, (request, session, current) =>
            {
                RequireReview(review, request.Origin, eligible);
                if (!current || session != review.Session)
                { throw new InvalidOperationException("Exact reviewed account, process or control session changed; review again."); }
                var requested = Audit(request, "model.local-admission", SecurityAuditOutcome.Requested);
                audit.Write(requested);
                try
                {
                    preferences.BeginWrite();
                    var choice = new LocalModelAdmissionPreference(review.Evidence.Digest, review.Evidence.ProfileId,
                        review.Subject.Account, time.GetUtcNow(), review.Evidence.ExpiresAt);
                    preferences.Save(choice);
                    if (preferences.ReadBack() != choice) { throw new InvalidDataException("Local model admission readback did not match."); }
                    audit.Write(requested.WithOutcome(SecurityAuditOutcome.Succeeded));
                    RequireReview(review, request.Origin, eligible);
                    preferences.Confirm();
                    if (preferences.Read() != choice) { throw new InvalidDataException("Local model admission confirmation changed."); }
                    broker.Admit(review.Evidence, review.Subject, () => !disposed && eligible() && review.Eligible()
                        && preferences.Read() == choice, lifetime.Token);
                    issued = null;
                    Status = "Exact local runtime admitted for this host lifetime. Choose its model in service settings. Revocation, restart or identity/evidence loss requires review again.";
                    return true;
                }
                catch
                {
                    audit.Write(requested.WithOutcome(SecurityAuditOutcome.Unknown));
                    throw;
                }
            }, linked.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Hold(exception);
            throw;
        }
        finally { gate.Release(); }
    }

    internal async Task Revoke(RequestOrigin origin, Func<bool> eligible, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        token = linked.Token;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            // Close live execution before any fallible persistence or audit work.
            RevokeCurrent();
            issued = null;
            await admission.RunAsync(origin, eligible, (request, _) =>
            {
                var requested = Audit(request, "model.local-revoke", SecurityAuditOutcome.Requested);
                audit.Write(requested);
                try
                {
                    preferences.BeginWrite();
                    preferences.Remove();
                    audit.Write(requested.WithOutcome(SecurityAuditOutcome.Succeeded));
                    preferences.Confirm();
                    Status = "Local model admission revoked. No process was stopped or installed. Review independently qualified evidence before later use.";
                    return true;
                }
                catch
                {
                    audit.Write(requested.WithOutcome(SecurityAuditOutcome.Unknown));
                    throw;
                }
            }, token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Hold(exception);
            throw;
        }
        finally { gate.Release(); }
    }

    private void RequireReview(OllamaAdmissionReview review, RequestOrigin origin, Func<bool> eligible)
    {
        if (!ReferenceEquals(review, issued)
            || origin != review.Origin || time.GetUtcNow() >= review.ExpiresAt || !review.Evidence.IsCurrent(time.GetUtcNow())
            || !eligible() || !review.Eligible())
        { throw new InvalidOperationException("Local model review is stale, foreign or no longer admitted; review again."); }
    }

    private static SecurityAuditEvent Audit(HostRequest request, string action, SecurityAuditOutcome outcome) =>
        new(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation, action, outcome,
            request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            request.SessionId.Value.ToString("D"));

    private void RevokeCurrent()
    {
        broker.Revoke();
    }

    private void Hold(Exception exception)
    {
        RevokeCurrent();
        issued = null;
        Status = "Local admission is unavailable or unconfirmed. Inspect diagnostics/audit, revoke the saved choice, and review again. No automatic retry or fallback.";
        Failed(logger, exception.GetType().Name);
    }

    public ValueTask DisposeAsync()
    {
        lock (disposalGate) { return new(disposal ??= Close()); }
    }

    private async Task Close()
    {
        disposed = true;
        await lifetime.CancelAsync().ConfigureAwait(false);
        await gate.WaitAsync().ConfigureAwait(false);
        try { RevokeCurrent(); issued = null; }
        finally { gate.Release(); lifetime.Dispose(); gate.Dispose(); }
    }
}