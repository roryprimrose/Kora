using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>Host-owned volatile applicability; no retained definition or review restores authority.</summary>
public sealed class SessionInstructionScopeBroker : IDisposable
{
    public const string Boundary = "Only subsequent ordinary local text requests in this exact Active session. "
        + "No tools, hosted transfer, questions, skills, agents, attachments, memory or history. "
        + "Guidance is lower-trust content, not host policy. Confirm separately within two minutes. "
        + "Stop applying removes future admission immediately; an already captured answer may finish. "
        + "Session/source changes, disable, privacy, call or ownership changes retire applicability. "
        + "Nothing is saved or restored at startup. An unavailable local runtime remains unavailable.";

    private readonly ScopedInstructionUseBroker sources;
    private readonly SessionWorkspaceService workspace;
    private readonly ISecurityAuditLog audit;
    private readonly TimeProvider time;
    private readonly Lock gate = new();
    private readonly Dictionary<HostId<SessionIdentity>, Binding> bindings = [];
    private bool disposed;
    private long reviewEpoch;

    public SessionInstructionScopeBroker(ScopedInstructionUseBroker sources, SessionWorkspaceService workspace,
        ISecurityAuditLog audit, TimeProvider time)
    {
        this.sources = sources; this.workspace = workspace; this.audit = audit; this.time = time;
        workspace.SessionLifecycleChanged += RetireSession;
        workspace.SessionRetired += RetireSession;
        workspace.SessionMetadataChanging += RetireSession;
    }

    public IReadOnlyList<ArtifactDefinition> Choices => sources.Choices;

    public async Task<SessionInstructionScopeReview> ReviewAsync(SessionWorkspaceEntry target, ArtifactDefinition selected,
        Func<bool> hostEligible, Func<bool> reviewEligible, CancellationToken token,
        RequestOrigin origin = RequestOrigin.LocalUi)
    {
        long epoch;
        lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); epoch = reviewEpoch; }
        var source = await sources.ReviewSourceAsync(target, selected, hostEligible, token, origin).ConfigureAwait(false);
        bool Current() { lock (gate) { return !disposed && reviewEpoch == epoch && reviewEligible(); } }
        var review = new SessionInstructionScopeReview(this, source, time.GetUtcNow(), Current);
        if (!review.CanConfirm) { review.Retire(); throw new InvalidOperationException("The original scope review was revoked."); }
        return review;
    }

    internal bool IsReviewCurrent(SessionInstructionScopeReview review)
    {
        lock (gate)
        {
            return !disposed && ReferenceEquals(review.Owner, this) && Volatile.Read(ref review.Consumed) == 0
                && time.GetUtcNow() >= review.CreatedAt && time.GetUtcNow() < review.CreatedAt.AddMinutes(2)
                && review.Eligible() && review.Source.IsCurrent();
        }
    }

    public async Task ConfirmAsync(SessionInstructionScopeReview review, Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(review);
        SessionConversationService.RequireOriginalSource(HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi);
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), review.Target.Authority.SessionId,
            new(Guid.NewGuid()), review.Origin), HostActivityLayer.Application, HostOperation.Policy,
            review.Source.Trace is { } trace ? [new(trace)] : null);
        try
        {
            await review.Source.ValidateSourceAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            lock (gate)
            {
                if (!admitted() || !IsReviewCurrent(review)
                    || Interlocked.CompareExchange(ref review.Consumed, 1, 0) != 0)
                { throw new InvalidOperationException("The exact scope review expired, changed or was already consumed."); }
                var request = HostActivity.RequireCurrent().Request;
                var entry = Audit(request, "session.instruction-scope.apply");
                audit.Write(entry);
                try
                {
                    if (!admitted() || !review.Eligible() || !review.Source.IsCurrent())
                    { throw new InvalidOperationException("Scope admission changed during audit."); }
                    var binding = new Binding(review.Source);
                    bindings[review.Target.Authority.SessionId] = binding;
                    reviewEpoch++;
                    audit.Write(entry.WithOutcome(SecurityAuditOutcome.Succeeded));
                }
                catch
                {
                    bindings.Remove(review.Target.Authority.SessionId);
                    audit.Write(entry.WithOutcome(SecurityAuditOutcome.Failed, "apply-unconfirmed"));
                    throw;
                }
            }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch
        {
            review.Retire();
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    public void Stop(SessionWorkspaceEntry target, RequestOrigin origin, Func<bool> admitted)
    {
        SessionConversationService.RequireOriginalSource(origin);
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), target.Authority.SessionId,
            new(Guid.NewGuid()), origin), HostActivityLayer.Application, HostOperation.Policy);
        try
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!admitted()) { throw new InvalidOperationException("Stop requires current private original input."); }
                // Revocation cannot be rolled back by an audit failure.
                bindings.Remove(target.Authority.SessionId);
                reviewEpoch++;
                var entry = Audit(HostActivity.RequireCurrent().Request, "session.instruction-scope.stop");
                audit.Write(entry);
                try { audit.Write(entry.WithOutcome(SecurityAuditOutcome.Succeeded)); }
                catch { audit.Write(entry.WithOutcome(SecurityAuditOutcome.Failed, "stop-receipt-unconfirmed")); throw; }
            }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    public string Status(SessionWorkspaceEntry target)
    {
        lock (gate)
        {
            if (disposed) { return "Session instruction scope unavailable."; }
            if (!bindings.TryGetValue(target.Authority.SessionId, out var binding))
            { return "No session instruction applies. " + Boundary; }
            if (binding.Source.Target != target || !binding.Source.IsCurrent())
            {
                bindings.Remove(target.Authority.SessionId);
                return "Session instruction applicability retired; review again. " + Boundary;
            }
            return "Applying " + binding.Source.Name + " to " + (target.Metadata?.Name.Value ?? "this Active session")
                + ". " + Boundary;
        }
    }

    internal Func<bool> ReviewBareRequest(SessionWorkspaceEntry target)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (bindings.ContainsKey(target.Authority.SessionId))
            { throw new InvalidOperationException("Fresh expired-text review supports no session instruction scope. Use its independent current source-review route."); }
            var epoch = reviewEpoch;
            return () =>
            {
                lock (gate)
                {
                    return !disposed && reviewEpoch == epoch && !bindings.ContainsKey(target.Authority.SessionId);
                }
            };
        }
    }

    internal async Task<ScopedInstructionUseBroker.InstructionContextReview?> PrepareRequestAsync(
        SessionWorkspaceEntry target, string body, RequestOrigin origin, CancellationToken token)
    {
        Binding? binding;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            bindings.TryGetValue(target.Authority.SessionId, out binding);
        }
        if (binding is null) { return null; }
        try
        {
            if (binding.Source.Target != target) { throw new InvalidOperationException("The scoped session changed; review again."); }
            await binding.Source.ValidateSourceAsync(token).ConfigureAwait(false);
            lock (gate)
            {
                if (!IsInstalled(binding)) { throw new InvalidOperationException("Instruction applicability stopped before this request."); }
                return sources.CreateRequestReview(binding.Source, body, origin, capture =>
                {
                    lock (gate)
                    {
                        if (!IsInstalled(binding))
                        { throw new InvalidOperationException("Instruction applicability stopped before host capture."); }
                        capture();
                    }
                });
            }
        }
        catch
        {
            lock (gate)
            {
                if (IsInstalled(binding)) { bindings.Remove(target.Authority.SessionId); }
            }
            throw;
        }
    }

    internal bool IsRequestAdmitted(ScopedInstructionUseBroker.InstructionContextReview review)
    {
        lock (gate)
        {
            return !disposed && (review.IsExecutionCaptured
                || bindings.TryGetValue(review.Target.Authority.SessionId, out var binding)
                    && ReferenceEquals(binding.Source, review.Source));
        }
    }

    private bool IsInstalled(Binding binding) => !disposed
        && bindings.TryGetValue(binding.Source.Target.Authority.SessionId, out var actual) && ReferenceEquals(actual, binding);

    private static SecurityAuditEvent Audit(HostRequest request, string action) =>
        new(request.RequestId.Value, SecurityAuditCategory.ProtectedOperation, action, SecurityAuditOutcome.Requested,
            request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            "session." + request.SessionId.Value.ToString("N"));

    public void RetireReviews() { lock (gate) { reviewEpoch++; } }
    public void RetireAll() { lock (gate) { bindings.Clear(); reviewEpoch++; } }
    private void RetireSession(HostId<SessionIdentity> session)
    { lock (gate) { bindings.Remove(session); reviewEpoch++; } }
    public void Dispose()
    {
        lock (gate) { if (disposed) { return; } disposed = true; bindings.Clear(); reviewEpoch++; }
        workspace.SessionLifecycleChanged -= RetireSession;
        workspace.SessionRetired -= RetireSession;
        workspace.SessionMetadataChanging -= RetireSession;
    }
    private sealed record Binding(ScopedInstructionUseBroker.InstructionSourceReview Source);
}
