using System.Runtime.CompilerServices;

using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Application.Dependencies;

/// <summary>Issues and consumes volatile host capabilities atomically; records alone cannot allocate resources.</summary>
internal sealed class RuntimeResourceAllocator(Guid owner, TimeProvider time, ISecurityAuditLog audit,
    IRuntimeCapacityQualification? qualification)
{
    private readonly Lock gate = new();
    private readonly List<Lease> leases = [];
    private readonly ConditionalWeakTable<ModelTurn, Lease> issued = new();
    private readonly List<ModelTurn> waiting = [];
    private readonly HashSet<RuntimeResourceId> quarantined = [];
    private bool exclusiveQuarantine;
    private TaskCompletionSource changed = NewSignal();
    internal const int MaximumWaiters = 10;

    internal bool IsIdle { get { lock (gate) { return leases.Count == 0 && waiting.Count == 0; } } }
    internal bool OwnsSession(HostId<SessionIdentity> session)
    {
        lock (gate)
        {
            return leases.Any(item => item.Turn.Provenance.Request.SessionId == session)
                || waiting.Any(item => item.Provenance.Request.SessionId == session);
        }
    }

    internal async Task WaitForQuiescenceAsync(CancellationToken token)
    {
        while (true)
        {
            Task signal;
            lock (gate)
            {
                if (IsIdle) { return; }
                signal = changed.Task;
            }
            await signal.WaitAsync(token).ConfigureAwait(false);
        }
    }

    internal sealed class Lease
    {
        internal required ModelTurn Turn { get; init; }
        internal required RuntimeCapacityEvidence? Evidence { get; init; }
        internal bool Joined { get; set; }
        internal bool FailedJoin { get; set; }
        internal bool Joining { get; set; }
        internal TaskCompletionSource Completion { get; } = NewSignal();
    }

    internal async Task<Lease?> AcquireAsync(ModelTurn turn, Func<bool> admitted, CancellationToken token)
    {
        if (turn.Owner != owner)
        { throw new InvalidOperationException("This turn belongs to another runtime issuer."); }
        if (issued.TryGetValue(turn, out _))
        { throw new InvalidOperationException("This exact turn already consumed its resource capability."); }
        var evidence = Observe(turn);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, evidence?.Revoked ?? CancellationToken.None);
        var queued = false;
        CancellationTokenSource? deadline = null;
        CancellationTokenSource? waitingCancellation = null;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (linked.IsCancellationRequested) { return null; }
                Task? signal = null;
                Lease? reserved = null;
                lock (gate)
                {
                    if (!admitted() || !Current(turn, evidence) || Quarantined(evidence)) { return null; }
                    if (!queued && OwnsSession(turn.Provenance.Request.SessionId)) { return null; }
                    if ((!queued && waiting.Count == 0 || queued && ReferenceEquals(waiting[0], turn))
                        && Available(turn, evidence))
                    {
                        reserved = new Lease { Turn = turn, Evidence = evidence };
                        leases.Add(reserved);
                    }
                    else
                    {
                        // Legacy qualification proves only one execution, never physical-resource independence.
                        if (evidence is null || !queued && waiting.Count >= MaximumWaiters) { return null; }
                        if (!queued)
                        {
                            var remaining = turn.Context.ExpiresAt - time.GetUtcNow();
                            if (remaining <= TimeSpan.Zero) { return null; }
                            deadline = new(remaining < TimeSpan.FromSeconds(15) ? remaining : TimeSpan.FromSeconds(15), time);
                            waitingCancellation = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, deadline.Token);
                            waiting.Add(turn);
                            queued = true;
                        }
                        signal = changed.Task;
                    }
                }
                // Typed audit can enter the authority store, whose commit fence reads this allocator.
                if (reserved is not null)
                { return Issue(reserved, admitted); }
                try { await signal!.WaitAsync(waitingCancellation!.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested && waitingCancellation!.IsCancellationRequested)
                { return null; }
            }
        }
        finally
        {
            waitingCancellation?.Dispose();
            deadline?.Dispose();
            if (queued)
            {
                lock (gate) { waiting.Remove(turn); Pulse(); }
            }
        }
    }

    internal bool IsCurrent(Lease lease)
    {
        lock (gate)
        {
            return issued.TryGetValue(lease.Turn, out var actual) && ReferenceEquals(actual, lease)
                && !lease.FailedJoin && !Quarantined(lease.Evidence)
                && Current(lease.Turn, lease.Evidence);
        }
    }

    internal void Join(Lease lease, bool dispatched)
    {
        bool failed;
        lock (gate)
        {
            if (!issued.TryGetValue(lease.Turn, out var actual) || !ReferenceEquals(actual, lease))
            { throw new InvalidOperationException("Resource release requires this issuer's exact owned lease."); }
            if (lease.Joined || lease.FailedJoin || lease.Joining) { return; }
            lease.Joining = true;
            failed = dispatched && !lease.Turn.Registration.Adapter!.IsQuiescent;
            if (failed) { Quarantine(lease); }
        }
        if (failed)
        {
            Record(lease.Turn, "runtime.resource.quarantine", SecurityAuditOutcome.Unknown);
            throw new InvalidOperationException("The owned runtime did not confirm physical resource termination.");
        }
        try
        {
            var requested = Record(lease.Turn, "runtime.resource.release", SecurityAuditOutcome.Requested);
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Succeeded));
        }
        catch
        {
            lock (gate) { Quarantine(lease); }
            throw;
        }
        lock (gate)
        {
            leases.Remove(lease);
            lease.Joined = true;
            lease.Completion.TrySetResult();
            Pulse();
        }
    }

    private Lease? Issue(Lease lease, Func<bool> admitted)
    {
        try
        {
            var requested = Record(lease.Turn, "runtime.resource.acquire", SecurityAuditOutcome.Requested);
            bool current;
            lock (gate) { current = admitted() && Current(lease.Turn, lease.Evidence) && !Quarantined(lease.Evidence); }
            if (!current)
            {
                audit.Write(requested.WithOutcome(SecurityAuditOutcome.Denied));
                return null;
            }
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Succeeded));
            lock (gate)
            {
                if (admitted() && Current(lease.Turn, lease.Evidence) && !Quarantined(lease.Evidence))
                {
                    issued.Add(lease.Turn, lease);
                    return lease;
                }
            }
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Denied));
            return null;
        }
        finally
        {
            lock (gate)
            {
                if (!issued.TryGetValue(lease.Turn, out var actual) || !ReferenceEquals(actual, lease))
                { leases.Remove(lease); Pulse(); }
            }
        }
    }

    private RuntimeCapacityEvidence? Observe(ModelTurn turn) =>
        turn.Provenance.Selection.Provider == ModelProviderIdentity.Ollama && turn.Profile is { } profile
            ? qualification?.Observe(turn.Provenance.Selection, profile) : null;

    private bool Current(ModelTurn turn, RuntimeCapacityEvidence? evidence)
    {
        if (!turn.Registration.IsQualified(time.GetUtcNow())) { return false; }
        if (evidence is null) { return true; }
        var now = time.GetUtcNow();
        return ReferenceEquals(Observe(turn), evidence) && evidence.Selection == turn.Provenance.Selection
            && evidence.Profile == turn.Profile && evidence.RuntimeGeneration != Guid.Empty
            && evidence.ObservedAt <= now && now < evidence.ExpiresAt
            && evidence.Revoked.CanBeCanceled && !evidence.Revoked.IsCancellationRequested
            && !evidence.Resources.IsDefaultOrEmpty
            && evidence.Resources.All(item => item.Resource.Value != Guid.Empty)
            && evidence.Resources.Select(item => item.Resource).Distinct().Count() == evidence.Resources.Length
            && evidence.OwnsIndependentTransport(turn.Registration.Adapter!);
    }

    private bool Quarantined(RuntimeCapacityEvidence? evidence) =>
        exclusiveQuarantine || evidence is null && quarantined.Count != 0
        || evidence is not null && evidence.Resources.Any(item => quarantined.Contains(item.Resource));

    private bool Available(ModelTurn turn, RuntimeCapacityEvidence? evidence)
    {
        if (leases.Count >= 2) { return false; }
        if (leases.Count == 0) { return turn.Registration.Adapter!.IsQuiescent; }
        if (evidence is null || leases.Any(item => item.Evidence is null)) { return false; }
        if (leases.Any(item => ReferenceEquals(item.Turn.Registration.Adapter, turn.Registration.Adapter)
            || item.Turn.Provenance.Request.SessionId == turn.Provenance.Request.SessionId)) { return false; }
        foreach (var lease in leases)
        {
            foreach (var resource in evidence.Resources)
            {
                if (lease.Evidence!.Resources.Any(other => other.Resource == resource.Resource
                    && (other.Exclusive || resource.Exclusive || !ReferenceEquals(lease.Evidence, evidence))))
                { return false; }
            }
        }
        return turn.Registration.Adapter!.IsQuiescent;
    }

    private SecurityAuditEvent Record(ModelTurn turn, string action, SecurityAuditOutcome outcome)
    {
        var entry = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation, action,
            outcome, SecurityAuditInitiator.System, turn.Provenance.Turn.Value.ToString("D"));
        audit.Write(entry);
        return entry;
    }

    private void Quarantine(Lease lease)
    {
        lease.FailedJoin = true;
        if (lease.Evidence is null) { exclusiveQuarantine = true; }
        else
        {
            foreach (var resource in lease.Evidence.Resources) { quarantined.Add(resource.Resource); }
        }
        leases.Remove(lease);
        lease.Completion.TrySetResult();
        Pulse();
    }

    private void Pulse()
    {
        var previous = changed;
        changed = NewSignal();
        previous.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}