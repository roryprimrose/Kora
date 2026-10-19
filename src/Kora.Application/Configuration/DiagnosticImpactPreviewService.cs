using System.Diagnostics;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

/// <summary>Observation only: no control-session admission, durable intent, policy activation or evidence writes.</summary>
public sealed class DiagnosticImpactPreviewService : IDisposable
{
    private static readonly ActivitySource ObservationSource = new("Kora.Application",
        typeof(HostActivity).Assembly.GetName().Version!.ToString());
    private readonly IDiagnosticRetentionImpactReader reader;
    private readonly IDiagnosticRetentionPreferences preferences;
    private readonly DiagnosticRetentionConfigurationService configuration;
    private readonly TimeProvider time;
    private readonly Lock gate = new();
    private DiagnosticImpactPreviewState state = new("Select shorter diagnostic days, then explicitly Preview. Nothing is saved or removed.");
    private Admission? admitted;
    private CancellationTokenSource? scan;
    private ITimer? expiry;
    private long revision;
    private long publishedAt;
    private int notifying;
    private bool disposed;

    public DiagnosticImpactPreviewService(IDiagnosticRetentionImpactReader reader, IDiagnosticRetentionPreferences preferences,
        DiagnosticRetentionConfigurationService configuration, TimeProvider? timeProvider = null)
    {
        this.reader = reader;
        this.preferences = preferences;
        this.configuration = configuration;
        time = timeProvider ?? TimeProvider.System;
        reader.Changed += OnSourceChanged;
        configuration.Changed += OnSourceChanged;
        configuration.ObservationChanged += OnSourceChanged;
    }

    public event EventHandler? Changed;
    private void OnSourceChanged(object? sender, EventArgs args) => Retire("Source or configuration observation changed; preview retired.");

    public DiagnosticImpactPreviewState Get()
    {
        lock (gate)
        {
            var expected = revision;
            if (admitted is { } admission && !Current(expected, state.Result, admission) && expected == revision)
            {
                RetireLocked("Preview retired; private source, host or confirmed configuration changed.");
            }
            return state;
        }
    }

    private bool Current(long expected, DiagnosticImpactSnapshot? result, Admission admission)
    {
        var preference = admission.Observation;
        notifying++;
        try
        {
            return admission.Eligible() && configuration.IsCurrentObservation(preference)
                && preferences.Load() == preference.Saved && preferences.ReadBack() == preference.Saved
                && (result is null || (time.GetElapsedTime(publishedAt) < DiagnosticRetentionImpact.PreviewLifetime
                    && (result.Snapshot is null || reader.IsCurrent(result))))
                && expected == revision;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
        finally { notifying--; }
    }

    private bool SnapshotCurrent(DiagnosticImpactSnapshot result)
    {
        notifying++;
        try { return reader.IsCurrent(result); }
        finally { notifying--; }
    }

    public async Task PreviewAsync(string value, Func<bool> remainsEligible, CancellationToken token = default)
    {
        using var timeout = new CancellationTokenSource(DiagnosticRetentionImpact.ScanTimeout, time);
        long expected;
        CancellationTokenSource cancellation;
        DiagnosticRetentionDays proposed;
        DateTimeOffset cutoff;
        HostRequest request;
        Admission admission;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (notifying != 0) { throw new InvalidOperationException("Preview cannot reenter a notification."); }
            RetireLocked("Preview unavailable; choose exact shorter days under confirmed configuration.");
            expected = revision;
            proposed = DiagnosticRetentionDays.Parse(value);
            admission = new(remainsEligible, configuration.GetObservation());
            admitted = admission;
            if (admission.Observation.State.Effective is not { } effective || proposed.Days >= effective.Days
                || !Current(expected, null, admission))
            {
                RetireLocked("Preview unavailable; confirmed configuration and current owning unlocked Settings are required.");
                throw new InvalidOperationException("Preview requires a shorter value and exact current configuration readback.");
            }
            request = HostRequest.Create(RequestOrigin.LocalUi);
            cutoff = time.GetUtcNow();
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
            scan = cancellation;
            state = new("Reading bounded private ordinary diagnostic metadata only.", proposed.Days, cutoff, Scanning: true);
        }

        HostActivity.ConfigureW3C();
        var previous = Activity.Current;
        Activity.Current = null;
        // Not a HostActivity: emitting this observation must not commit a self-invalidating evidence span.
        using var activity = ObservationSource.StartActivity("diagnostics.retention.preview", ActivityKind.Internal,
            default(ActivityContext), tags: [new("kora.request.id", request.RequestId.Value)]);
        try
        {
            Notify();
            cancellation.Token.ThrowIfCancellationRequested();
            var result = await reader.ReadAsync(proposed, cutoff, request, cancellation.Token).AsTask()
                .WaitAsync(DiagnosticRetentionImpact.ScanTimeout, time, cancellation.Token).ConfigureAwait(false);
            DiagnosticRetentionImpact.Validate(result, proposed, cutoff);
            lock (gate)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (!Current(expected, null, admission) || result.Snapshot is not null && !SnapshotCurrent(result)
                    || expected != revision)
                {
                    throw new InvalidOperationException("Diagnostic preview admission or snapshot changed before publication.");
                }
                publishedAt = time.GetTimestamp();
                state = new(result.Logs.Status == DiagnosticImpactStatus.Complete
                    && result.Spans.Status == DiagnosticImpactStatus.Complete
                    ? "Complete hypothetical observation only; no cleanup promised."
                    : "Incomplete/unavailable source; no complete impact preview or cleanup promise.",
                    proposed.Days, cutoff, Result: result);
                var timer = time.CreateTimer(_ => Expire(expected), null,
                    DiagnosticRetentionImpact.PreviewLifetime, Timeout.InfiniteTimeSpan);
                if (expected != revision || disposed)
                {
                    timer.Dispose();
                    throw new InvalidOperationException("Preview lifetime changed during publication.");
                }
                expiry = timer;
            }
            activity?.SetStatus(ActivityStatusCode.Ok);
            Notify();
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            var retired = false;
            lock (gate)
            {
                if (expected == revision)
                {
                    RetireLocked("Preview failed, cancelled or timed out; no result. Explicitly Preview again.");
                    retired = true;
                }
            }
            if (retired) { Notify(); }
            throw;
        }
        finally
        {
            lock (gate) { if (ReferenceEquals(scan, cancellation)) { scan = null; } }
            await cancellation.CancelAsync().ConfigureAwait(false);
            cancellation.Dispose();
            Activity.Current = previous;
        }
    }

    public void Retire(string reason)
    {
        lock (gate) { RetireLocked(reason); }
        Notify();
    }

    private void Expire(long expected)
    {
        lock (gate)
        {
            if (disposed || expected != revision) { return; }
            RetireLocked("Preview expired; explicitly Preview again.");
        }
        Notify();
    }

    private void RetireLocked(string reason)
    {
        revision = checked(revision + 1);
        var cancellation = scan;
        scan = null;
        var timer = expiry;
        expiry = null;
        admitted = null;
        state = new(reason);
        // Clear all old state before invoking cancellation callbacks; no callback can revive that generation.
        notifying++;
        try { timer?.Dispose(); cancellation?.Cancel(); }
        finally { notifying--; }
    }

    private void Notify()
    {
        lock (gate) { notifying++; }
        try { Changed?.Invoke(this, EventArgs.Empty); }
        finally { lock (gate) { notifying--; } }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) { return; }
            disposed = true;
            reader.Changed -= OnSourceChanged;
            configuration.Changed -= OnSourceChanged;
            configuration.ObservationChanged -= OnSourceChanged;
            RetireLocked("Preview closed.");
        }
    }

    private sealed record Admission(Func<bool> Eligible, DiagnosticRetentionObservation Observation);
}
