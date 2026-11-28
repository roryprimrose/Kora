using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;

namespace Kora.Application.Hosting;

/// <summary>
/// Owns one running ordinary turn's check-in schedule. A due check-in is posted while the turn keeps running;
/// host waits pause the schedule. Unanswered check-ins retire with the turn. Issuance and every answer use
/// the trusted audit log; a missing audit log fails closed by stopping the turn instead of running silently.
/// </summary>
internal sealed class OrdinaryTurnCheckIns : IDisposable
{
    private readonly Lock gate = new();
    private readonly ModelTurnLifetime lifetime;
    private readonly OrdinaryTurnDeadline interval;
    private readonly TimeProvider time;
    private readonly ISecurityAuditLog? audit;
    private readonly string target;
    private readonly Action<ConversationCheckIn?> present;
    private readonly Action unavailable;
    // Timer callbacks have no ambient host context; audit and presentation link to the owning turn instead.
    private readonly Func<HostActivity> continuation =
        HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Policy);
    private readonly ITimer timer;
    private readonly long startedAt;
    private readonly TimeSpan startPaused;
    private long markAt;
    private TimeSpan markPaused;
    private TimeSpan due;
    private ConversationCheckIn? pending;
    private SecurityAuditEvent? issued;
    private bool indefinite;
    private bool retired;
    private bool disposed;

    internal OrdinaryTurnCheckIns(ModelTurnLifetime lifetime, OrdinaryTurnDeadline interval, TimeProvider time,
        ISecurityAuditLog? audit, Guid target, Action<ConversationCheckIn?> present, Action unavailable)
    {
        this.lifetime = lifetime;
        this.interval = interval;
        this.time = time;
        this.audit = audit;
        this.target = target.ToString("D");
        this.present = present;
        this.unavailable = unavailable;
        startedAt = markAt = time.GetTimestamp();
        startPaused = markPaused = lifetime.PausedDuration;
        due = interval.Duration;
        timer = time.CreateTimer(_ => OnDue(), null, due, Timeout.InfiniteTimeSpan);
        lifetime.HostWaitChanged += OnHostWaitChanged;
    }

    internal ConversationCheckIn? Pending { get { lock (gate) { return pending; } } }

    private TimeSpan ActiveSince(long timestamp, TimeSpan paused)
    {
        var active = time.GetElapsedTime(timestamp) - (lifetime.PausedDuration - paused);
        return active > TimeSpan.Zero ? active : TimeSpan.Zero;
    }

    private void OnDue()
    {
        using var activity = continuation();
        ConversationCheckIn? posted = null;
        var failClosed = false;
        lock (gate)
        {
            if (retired || indefinite || pending is not null || lifetime.IsWaitingForHost)
            {
                activity.Complete(HostOperationOutcome.Cancelled);
                return;
            }
            var remaining = due - ActiveSince(markAt, markPaused);
            if (remaining > TimeSpan.Zero)
            {
                timer.Change(remaining, Timeout.InfiniteTimeSpan);
                activity.Complete(HostOperationOutcome.Completed);
                return;
            }
            if (audit is null)
            {
                retired = true;
                failClosed = true;
            }
            else
            {
                var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
                    "conversation.check-in", SecurityAuditOutcome.Requested, SecurityAuditInitiator.System, target);
                audit.Write(requested);
                issued = requested;
                pending = posted = new(Guid.NewGuid(), interval, ActiveSince(startedAt, startPaused));
            }
        }
        if (failClosed) { unavailable(); }
        else { present(posted); }
        activity.Complete(failClosed ? HostOperationOutcome.Failed : HostOperationOutcome.Completed);
    }

    private void OnHostWaitChanged(bool waiting)
    {
        lock (gate)
        {
            if (retired || indefinite || pending is not null) { return; }
            var remaining = due - ActiveSince(markAt, markPaused);
            timer.Change(waiting ? Timeout.InfiniteTimeSpan : remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero,
                Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Applies a keep-going choice to the exact pending check-in, or throws when it is stale.</summary>
    internal void Answer(Guid checkIn, ConversationCheckInChoice choice, SecurityAuditInitiator initiator)
    {
        if (choice.Operation == ConversationCheckInOperation.Stop)
        { throw new InvalidOperationException("Stop uses the running-conversation cancellation path."); }
        lock (gate)
        {
            var requested = Request(checkIn, choice, initiator);
            pending = null;
            audit!.Write(issued!.WithOutcome(SecurityAuditOutcome.Succeeded, choice.ReasonCode));
            issued = null;
            indefinite = choice.Operation == ConversationCheckInOperation.KeepRunning;
            markAt = time.GetTimestamp();
            markPaused = lifetime.PausedDuration;
            due = choice.Minutes?.Duration ?? interval.Duration;
            if (!indefinite && !lifetime.IsWaitingForHost) { timer.Change(due, Timeout.InfiniteTimeSpan); }
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Succeeded, requested.ReasonCode));
        }
        present(null);
    }

    /// <summary>Records a stop answer for the exact pending check-in before cancellation is requested.</summary>
    internal Action<bool> BeginStop(Guid checkIn, SecurityAuditInitiator initiator)
    {
        SecurityAuditEvent requested;
        lock (gate)
        {
            requested = Request(checkIn, new(ConversationCheckInOperation.Stop), initiator);
        }
        return accepted =>
        {
            lock (gate)
            {
                audit!.Write(requested.WithOutcome(accepted ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Failed, requested.ReasonCode));
                if (accepted && issued is not null && pending?.Id == checkIn)
                {
                    audit.Write(issued.WithOutcome(SecurityAuditOutcome.Succeeded, "stop"));
                    issued = null;
                    pending = null;
                    retired = true;
                }
            }
        };
    }

    private SecurityAuditEvent Request(Guid checkIn, ConversationCheckInChoice choice, SecurityAuditInitiator initiator)
    {
        // Every retirement also clears the pending check-in.
        if (pending is null || pending.Id != checkIn)
        { throw new InvalidOperationException("Only the exact current check-in of this running turn can be answered."); }
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "conversation.check-in-answer", SecurityAuditOutcome.Requested, initiator, target, reasonCode: choice.ReasonCode);
        audit!.Write(requested);
        return requested;
    }

    /// <summary>Retires the schedule when the turn ends; an unanswered check-in is closed, never replayed.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) { return; }
            disposed = retired = true;
            if (issued is not null) { audit!.Write(issued.WithOutcome(SecurityAuditOutcome.Cancelled, "turn-ended")); }
            issued = null;
            pending = null;
        }
        timer.Dispose();
        lifetime.HostWaitChanged -= OnHostWaitChanged;
    }
}
