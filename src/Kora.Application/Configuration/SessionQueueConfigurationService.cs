using Kora.Application.Communication;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

public sealed class SessionQueueConfigurationService(
    ISessionQueuePreferences preferences, AudioControlAdmission admission, ISecurityAuditLog auditLog) : IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly SemaphoreSlim commits = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private SessionQueueConfigurationState state = new(null, null, 0, "Refresh fixed local-version queue settings in the owning unlocked host.");
    private SessionQueueConfigurationProposal? proposal;
    private WorkSessionAuthorization? session;
    private RequestOrigin origin;
    private Func<bool>? eligible;
    private bool applying;
    private bool publishing;
    private bool disposed;
    private Task? disposal;

    public event EventHandler? Changed;
    public SessionQueueConfigurationState Get() { lock (gate) { return state; } }
    internal bool IsCurrent(SessionQueueLimits limits)
    {
        lock (gate)
        {
            if (disposed || applying || !state.Available || !ReferenceEquals(state.Effective, limits)) { return false; }
            if (preferences.Load() != state.Saved)
            {
                HoldUnavailable();
                return false;
            }
            return true;
        }
    }

    public void Observe()
    {
        if (!commits.Wait(0)) { throw new InvalidOperationException("Queue settings are in use by an admission/observation transaction."); }
        try
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                try { ObserveConfirmed(preferences.Load()); }
                catch { HoldUnavailable(); throw; }
            }
        }
        finally { commits.Release(); }
    }

    private void ObserveConfirmed(SessionQueuePreferences saved)
    {
        if (state.Available && state.Saved == saved) { return; }
        proposal = null;
        state = new(saved, saved.Limits, checked(state.Revision + 1), null);
        Publish();
    }

    // Only short admission/observation transactions hold this gate, never an admitted callback or dispatch batch.
    // Thus each enqueue captures one confirmed set of limits, including its future-only lifetime.
    // Preference edits never cancel, extend or reinterpret existing work.
    internal async Task<T> WithLimitsAsync<T>(Func<SessionQueueLimits, Task<T>> operation, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await commits.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            SessionQueueLimits limits;
            long revision;
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                RequireConfirmed();
                limits = state.Effective!;
                revision = state.Revision;
            }
            var result = await operation(limits).ConfigureAwait(false);
            lock (gate)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (state.Revision != revision) { throw new InvalidOperationException("Queue settings changed during observation/admission."); }
                RequireConfirmed();
            }
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            HoldUnavailable();
            throw;
        }
        finally { commits.Release(); }
    }

    private void RequireConfirmed()
    {
        if (!state.Available)
        {
            throw new InvalidOperationException("Queue preferences are unavailable or changed; explicitly refresh before new admission.");
        }
        if (preferences.Load() != state.Saved)
        {
            HoldUnavailable();
            throw new InvalidOperationException("Saved queue preferences changed; explicitly refresh before new admission.");
        }
    }

    public async Task RefreshAsync(RequestOrigin originalOrigin, Func<bool> remainsEligible, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await commits.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var observed = await admission.RunAsync(originalOrigin, remainsEligible, (request, authority) =>
            {
                lock (gate)
                {
                    var saved = preferences.Load();
                    proposal = null;
                    state = state with { Revision = checked(state.Revision + 1) };
                    session = authority;
                    origin = request.Origin;
                    eligible = remainsEligible;
                    return saved;
                }
            }, linked.Token).ConfigureAwait(false);
            lock (gate)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (!remainsEligible() || preferences.Load() != observed)
                {
                    throw new InvalidOperationException("Queue discovery admission or saved state changed before its receipt.");
                }
                ObserveConfirmed(observed);
            }
        }
        catch { HoldUnavailable(); throw; }
        finally { commits.Release(); }
    }

    public SessionQueueConfigurationProposal Propose(SessionQueueOption option, string? value,
        long expectedRevision, long callRevision)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (applying || publishing || expectedRevision != state.Revision || session is null
                || eligible is null || !eligible() || !state.Available)
            {
                throw new InvalidOperationException("Refresh queue settings under the current original input and choose again.");
            }
            proposal = new(state.Saved!.With(option, value), option, expectedRevision, callRevision, session, origin, eligible);
            return proposal;
        }
    }

    public async Task<bool> ApplyAsync(SessionQueueConfigurationProposal candidate, RequestOrigin originalOrigin,
        SecurityAuditInitiator initiator, CallCommunicationPolicy callPolicy, Func<bool> remainsEligible, CancellationToken token)
    {
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || initiator == SecurityAuditInitiator.VoiceCommand && originalOrigin != RequestOrigin.ActivatedVoice)
        {
            throw new InvalidOperationException("Original activated input provenance is required.");
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await commits.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var accepted = await admission.RunAsync(originalOrigin, remainsEligible, (request, authority) =>
            {
                lock (gate)
                {
                    if (applying || publishing || !ReferenceEquals(proposal, candidate) || candidate.Session != authority
                        || candidate.Origin != request.Origin || candidate.Revision != state.Revision
                        || !candidate.Eligible() || !state.Available || preferences.Load() != state.Saved)
                    {
                        throw new InvalidOperationException("Queue proposal, saved state or original admission changed.");
                    }
                    applying = true;
                    var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                        candidate.Option == SessionQueueOption.PendingPerSession ? "configuration.queue-pending-per-session"
                            : candidate.Option == SessionQueueOption.PendingLifetimeMinutes ? "configuration.queue-pending-lifetime-minutes"
                            : "configuration.queue-execution-slots", SecurityAuditOutcome.Requested,
                        request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : initiator,
                        "preferences.device-local");
                    auditLog.Write(audit);
                    var denied = callPolicy.CommitVoiceSetting(request.Origin, candidate.CallRevision,
                        () => ReferenceEquals(HostActivity.RequireCurrent().Request, request)
                            && remainsEligible() && candidate.Eligible() && !linked.IsCancellationRequested
                            && ReferenceEquals(proposal, candidate) && candidate.Revision == state.Revision && state.Available, () =>
                        {
                            try
                            {
                                if (preferences.Load() != state.Saved)
                                {
                                    throw new InvalidDataException("Saved queue settings changed during requested audit; refresh before choosing again.");
                                }
                                preferences.BeginWrite();
                                preferences.Save(candidate.Value);
                                if (preferences.ReadBack() != candidate.Value)
                                {
                                    throw new InvalidDataException("Queue preference readback did not match the exact overrides.");
                                }
                            }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                            {
                                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed,
                                    exception is InvalidDataException ? "invalid-data" : exception is IOException ? "io-error" : "access-denied"));
                                throw;
                            }
                            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                            state = state with { Revision = checked(state.Revision + 1) };
                            proposal = null;
                        });
                    if (denied is { } reason)
                    {
                        auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Denied, reason.ToString().ToLowerInvariant()));
                        return false;
                    }
                    return true;
                }
            }, linked.Token).ConfigureAwait(false);
            lock (gate)
            {
                if (accepted)
                {
                    if (!remainsEligible() || !candidate.Eligible() || preferences.ReadBack() != candidate.Value)
                    {
                        throw new InvalidOperationException("Queue preference admission or readback changed before its completed control receipt.");
                    }
                    preferences.ConfirmWrite();
                    try
                    {
                        var confirmed = preferences.Load();
                        if (confirmed != candidate.Value || !remainsEligible() || !candidate.Eligible())
                        {
                            throw new InvalidDataException("Confirmed queue state or original admission changed before activation.");
                        }
                        applying = false;
                        ObserveConfirmed(confirmed);
                    }
                    catch
                    {
                        preferences.BeginWrite();
                        throw;
                    }
                }
            }
            return accepted;
        }
        catch { HoldUnavailable(); throw; }
        finally { lock (gate) { applying = false; } commits.Release(); }
    }

    public void HoldUnavailable()
    {
        lock (gate)
        {
            proposal = null;
            state = state with { Effective = null, Revision = checked(state.Revision + 1),
                Recovery = "Queue preferences or required evidence are unconfirmed. New admissions are held. Inspect saved state and audit/control receipts, explicitly repair, then refresh; no automatic retry, rollback or replay. Existing work/deadlines remain unchanged." };
            Publish();
        }
    }

    private void Publish()
    {
        publishing = true;
        try { Changed?.Invoke(this, EventArgs.Empty); }
        finally { publishing = false; }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate) { return new(disposal ??= CloseAsync()); }
    }

    private async Task CloseAsync()
    {
        disposed = true;
        await lifetime.CancelAsync().ConfigureAwait(false);
        await commits.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        commits.Dispose();
        lifetime.Dispose();
    }
}
