using Kora.Core.Hosting;

namespace Kora.Core.Dependencies;

/// <summary>
/// Host-owned active-time clock for one admitted model turn. While a blocking host wait (for example an
/// open permission card) is in progress every clock derived from this lifetime pauses: context expiry,
/// runtime deadlines, ordinary check-in intervals and transport conversation deadlines resume only after
/// the wait ends. Check-in turns have no fixed active limit; the host asks the user instead.
/// </summary>
/// <remarks>
/// This is timing policy only. It never confers session, tool, approval, egress or ownership authority,
/// and every existing admission, revalidation and audit check still applies while time is paused.
/// Tool callbacks without the owning turn use <see cref="Current"/> and wrap their blocking permission
/// wait in <c>using (ModelTurnLifetime.Current?.BeginHostWait()) { ... }</c>.
/// </remarks>
public sealed class ModelTurnLifetime
{
    private static readonly AsyncLocal<ModelTurnLifetime?> Ambient = new();
    private readonly Lock gate = new();
    private readonly TimeProvider time;
    private int waits;
    private long pauseStarted;
    private TimeSpan paused;
    private bool checkIns;

    public ModelTurnLifetime(HostRequest request, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(time);
        Request = request;
        this.time = time;
    }

    /// <summary>Raised with <see langword="true"/> when the first host wait starts and <see langword="false"/> when the last ends.</summary>
    public event Action<bool>? HostWaitChanged;

    public HostRequest Request { get; }

    /// <summary>The lifetime of the turn executing on this asynchronous flow, if the host entered one.</summary>
    public static ModelTurnLifetime? Current => Ambient.Value;

    /// <summary>Whether ordinary check-ins, not fixed active limits, govern this turn.</summary>
    public bool UsesCheckIns { get { lock (gate) { return checkIns; } } }

    public bool IsWaitingForHost { get { lock (gate) { return waits > 0; } } }

    /// <summary>Total paused time, including an in-progress host wait.</summary>
    public TimeSpan PausedDuration
    {
        get
        {
            lock (gate)
            {
                if (waits == 0) { return paused; }
                var current = time.GetElapsedTime(pauseStarted);
                return paused + (current > TimeSpan.Zero ? current : TimeSpan.Zero);
            }
        }
    }

    /// <summary>Returns the current lifetime only when it belongs to <paramref name="request"/>.</summary>
    public static ModelTurnLifetime? For(HostRequest request) =>
        Current is { } current && current.Request == request ? current : null;

    /// <summary>Host-only: binds <paramref name="lifetime"/> to the current asynchronous flow until disposal.</summary>
    public static IDisposable Enter(ModelTurnLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        var previous = Ambient.Value;
        Ambient.Value = lifetime;
        return new Scope(() => Ambient.Value = previous);
    }

    /// <summary>Irreversibly removes fixed active limits; the host's check-in schedule governs the turn instead.</summary>
    public void UseCheckIns()
    {
        lock (gate) { checkIns = true; }
    }

    /// <summary>
    /// Pauses every clock derived from this lifetime until the returned scope is disposed. Scopes nest;
    /// disposal is idempotent. Use it around a blocking user decision such as a permission card.
    /// </summary>
    public IDisposable BeginHostWait()
    {
        bool first;
        lock (gate)
        {
            first = waits++ == 0;
            if (first) { pauseStarted = time.GetTimestamp(); }
        }
        if (first) { HostWaitChanged?.Invoke(true); }
        return new Scope(EndHostWait);
    }

    /// <summary>The fixed <paramref name="expiresAt"/> moved forward by paused time, or no expiry for check-in turns.</summary>
    public DateTimeOffset Extend(DateTimeOffset expiresAt)
    {
        if (UsesCheckIns || expiresAt == DateTimeOffset.MaxValue) { return DateTimeOffset.MaxValue; }
        var delay = PausedDuration;
        return DateTimeOffset.MaxValue - expiresAt <= delay ? DateTimeOffset.MaxValue : expiresAt + delay;
    }

    /// <summary>Active time left at <paramref name="now"/> before <paramref name="expiresAt"/>; infinite for check-in turns.</summary>
    public TimeSpan Remaining(DateTimeOffset expiresAt, DateTimeOffset now)
    {
        var effective = Extend(expiresAt);
        if (effective == DateTimeOffset.MaxValue) { return Timeout.InfiniteTimeSpan; }
        var remaining = effective - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private void EndHostWait()
    {
        bool last;
        lock (gate)
        {
            last = --waits == 0;
            if (last)
            {
                var current = time.GetElapsedTime(pauseStarted);
                paused += current > TimeSpan.Zero ? current : TimeSpan.Zero;
            }
        }
        if (last) { HostWaitChanged?.Invoke(false); }
    }

    private sealed class Scope(Action close) : IDisposable
    {
        private int closed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref closed, 1) == 0) { close(); }
        }
    }
}
