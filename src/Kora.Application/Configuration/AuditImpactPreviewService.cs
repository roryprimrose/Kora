using System.Diagnostics;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

/// <summary>Native observation only. No durable intent, control session, policy activation or audit write.</summary>
public sealed class AuditImpactPreviewService : IDisposable
{
    private static readonly ActivitySource ObservationSource = new("Kora.Application",
        typeof(HostActivity).Assembly.GetName().Version!.ToString());
    private readonly IAuditRetentionImpactReader reader;
    private readonly IAuditRetentionPreferences preferences;
    private readonly AuditRetentionConfigurationService configuration;
    private readonly TimeProvider time;
    private readonly Lock gate = new();
    private AuditImpactPreviewState state = new("Choose a shorter proposed value, then Preview. Apply-now/deletion unavailable.");
    private CancellationTokenSource? scan;
    private ITimer? expiry;
    private Func<bool>? eligible;
    private AuditRetentionState? observedPreference;
    private long revision;
    private bool disposed;

    public AuditImpactPreviewService(IAuditRetentionImpactReader reader, IAuditRetentionPreferences preferences,
        AuditRetentionConfigurationService configuration, TimeProvider? timeProvider = null)
    {
        this.reader = reader;
        this.preferences = preferences;
        this.configuration = configuration;
        time = timeProvider ?? TimeProvider.System;
        reader.Changed += OnSourceChanged;
        configuration.Changed += OnSourceChanged;
    }

    public event EventHandler? Changed;
    private void OnSourceChanged(object? sender, EventArgs args) => Retire("Source or preference changed; preview retired. Preview again.");

    public AuditImpactPreviewState Get()
    {
        lock (gate)
        {
            if (eligible is not null && !Current())
            {
                RetireLocked("Preview retired; source, preference or host state changed. Preview again.");
            }
            return state;
        }
    }

    private bool Current()
    {
        try
        {
            return eligible!() && configuration.IsCurrentObservation(observedPreference!)
                && preferences.Load() == observedPreference!.Saved && preferences.ReadBack() == observedPreference.Saved
                && (state.RequiredAuthority is not { Snapshot: not null } authority || reader.IsCurrent(authority))
                && (state.DiagnosticProjection is not { Snapshot: not null } projection || reader.IsCurrent(projection));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    public async Task PreviewAsync(string value, Func<bool> remainsEligible, CancellationToken token = default)
    {
        using var timeout = new CancellationTokenSource(AuditRetentionImpact.ScanTimeout, time);
        long requestRevision;
        CancellationTokenSource cancellation;
        HostRequest request;
        AuditRetentionDays proposed;
        DateTimeOffset cutoff;
        lock (gate)
        {
            RetireLocked("Preview unavailable; choose valid shorter days and an owning unlocked desktop.");
            ObjectDisposedException.ThrowIf(disposed, this);
            proposed = AuditRetentionDays.Parse(value);
            var saved = configuration.GetObservation();
            if (!remainsEligible() || saved.Effective is not { } effective || !configuration.IsCurrentObservation(saved)
                || proposed.Days >= effective.Days
                || preferences.Load() != saved.Saved || preferences.ReadBack() != saved.Saved)
            {
                throw new InvalidOperationException("Preview requires exact confirmed preference readback and a shorter value.");
            }
            observedPreference = saved;
            eligible = remainsEligible;
            requestRevision = revision;
            request = HostRequest.Create(RequestOrigin.LocalUi);
            cutoff = time.GetUtcNow();
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
            scan = cancellation;
            state = new("Scanning bounded committed audit metadata only.", proposed.Days, cutoff);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        // Observation correlation cannot establish HostActivity/audit authority or persist a self-invalidating store span.
        HostActivity.ConfigureW3C();
        var priorActivity = Activity.Current;
        Activity.Current = null;
        using var activity = ObservationSource.StartActivity("retention.preview", ActivityKind.Internal,
            default(ActivityContext), tags: [new("kora.request.id", request.RequestId.Value)]);
        try
        {
            var authority = await reader.ReadAsync(AuditImpactSource.RequiredAuthority, proposed, cutoff, request, cancellation.Token)
                .AsTask().WaitAsync(AuditRetentionImpact.ScanTimeout, time, cancellation.Token).ConfigureAwait(false);
            var projection = await reader.ReadAsync(AuditImpactSource.DiagnosticProjection, proposed, cutoff, request, cancellation.Token)
                .AsTask().WaitAsync(AuditRetentionImpact.ScanTimeout, time, cancellation.Token).ConfigureAwait(false);
            lock (gate)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (disposed || requestRevision != revision || !Current())
                {
                    throw new InvalidOperationException("Preview admission or source changed before its receipt.");
                }
                AuditRetentionImpact.Validate(authority, AuditImpactSource.RequiredAuthority, proposed, cutoff);
                AuditRetentionImpact.Validate(projection, AuditImpactSource.DiagnosticProjection, proposed, cutoff);
                if (authority.Snapshot is not null && !reader.IsCurrent(authority)
                    || projection.Snapshot is not null && !reader.IsCurrent(projection))
                {
                    throw new InvalidDataException("The exact store snapshot changed before preview publication.");
                }
                state = new(authority.Status == AuditImpactStatus.Complete && projection.Status == AuditImpactStatus.Complete
                    ? "Complete observation only. Apply-now/deletion unavailable."
                    : "Incomplete/unavailable source; no complete impact preview. Apply-now/deletion unavailable.",
                    proposed.Days, cutoff, authority, projection);
                expiry = time.CreateTimer(_ => Expire(requestRevision),
                    null, AuditRetentionImpact.PreviewLifetime, Timeout.InfiniteTimeSpan);
            }
            activity?.SetStatus(ActivityStatusCode.Ok);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            lock (gate)
            {
                if (requestRevision == revision) { RetireLocked("Preview failed/cancelled/timed out; no result. Preview again."); }
            }
            Changed?.Invoke(this, EventArgs.Empty);
            throw;
        }
        finally
        {
            lock (gate) { if (ReferenceEquals(scan, cancellation)) { scan = null; } }
            await cancellation.CancelAsync().ConfigureAwait(false);
            cancellation.Dispose();
            Activity.Current = priorActivity;
        }
    }

    public void Retire(string reason)
    {
        lock (gate) { RetireLocked(reason); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Expire(long expected)
    {
        lock (gate)
        {
            if (expected != revision) { return; }
            RetireLocked("Preview timed out; Preview again.");
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RetireLocked(string reason)
    {
        revision = checked(revision + 1);
        scan?.Cancel();
        expiry?.Dispose();
        expiry = null;
        eligible = null;
        observedPreference = null;
        state = new(reason);
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            RetireLocked("Preview closed.");
            reader.Changed -= OnSourceChanged;
            configuration.Changed -= OnSourceChanged;
        }
    }
}
