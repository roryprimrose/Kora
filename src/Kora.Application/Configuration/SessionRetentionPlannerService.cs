using System.Diagnostics;

using Kora.Core.Configuration;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Configuration;

/// <summary>Explicit native observation only: no durable intent, preference write, activity renewal or cleanup.</summary>
public sealed class SessionRetentionPlannerService : IDisposable
{
    private static readonly ActivitySource ObservationSource = new("Kora.Application",
        typeof(HostActivity).Assembly.GetName().Version!.ToString());
    private readonly ISessionRetentionPlannerReader reader;
    private readonly SessionRetentionConfigurationService configuration;
    private readonly TimeProvider time;
    private readonly Lock gate = new();
    private SessionRetentionPlannerState state = new("Choose a proposed archive/delete policy, then Preview. No policy or session changes.");
    private SessionRetentionConfigurationObservation? observed;
    private Func<bool>? eligible;
    private CancellationTokenSource? scan;
    private ITimer? expiry;
    private long revision;
    private bool disposed;

    public SessionRetentionPlannerService(ISessionRetentionPlannerReader reader,
        SessionRetentionConfigurationService configuration, TimeProvider? timeProvider = null)
    {
        this.reader = reader;
        this.configuration = configuration;
        time = timeProvider ?? TimeProvider.System;
        reader.Changed += OnChanged;
        configuration.Changed += OnChanged;
    }

    public event EventHandler? Changed;
    private void OnChanged(object? sender, EventArgs args) =>
        Retire("Source or preference changed; preview retired. Explicitly Preview again.");

    private bool Current()
    {
        try
        {
            var admission = eligible;
            var preference = observed;
            return admission is not null && preference is not null
                && admission() && configuration.IsCurrentObservation(preference)
                && (state.Result is not { Snapshot: not null } result || reader.IsCurrent(result))
                && ReferenceEquals(eligible, admission) && observed == preference
                && admission() && configuration.IsCurrentObservation(preference);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidDataException or InvalidOperationException)
        {
            return false;
        }
    }

    public SessionRetentionPlannerState Get()
    {
        lock (gate)
        {
            if (eligible is not null)
            {
                var current = Current();
                if (!current || state.ExpiresUtc is { } expires && time.GetUtcNow() >= expires)
                {
                    RetireLocked("Preview expired or its exact source/admission changed. Explicitly Preview again.");
                }
            }
            return state;
        }
    }

    public async Task PreviewAsync(SessionRetentionSettings proposed, SessionRetentionPlannerAdmission admission, Func<bool> remainsEligible,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(remainsEligible);
        ArgumentNullException.ThrowIfNull(admission);
        using var timeout = new CancellationTokenSource(SessionRetentionPlanner.ScanTimeout, time);
        long requestRevision;
        CancellationTokenSource cancellation;
        DateTimeOffset cutoff;
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        lock (gate)
        {
            RetireLocked("Preview unavailable; review a valid proposed policy on the owning unlocked native surface.");
            ObjectDisposedException.ThrowIf(disposed, this);
            proposed.Validate();
            var preference = configuration.GetObservation();
            if (!remainsEligible())
            {
                throw new InvalidOperationException("Retention planning requires current native ownership and privacy admission.");
            }
            observed = preference;
            eligible = remainsEligible;
            requestRevision = revision;
            cutoff = time.GetUtcNow();
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
            scan = cancellation;
            state = new("Scanning bounded retention and authority metadata only.", proposed, cutoff, preference, Admission: admission);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        HostActivity.ConfigureW3C();
        var priorActivity = Activity.Current;
        Activity.Current = null;
        using var activity = ObservationSource.StartActivity("session.retention.preview", ActivityKind.Internal,
            default(ActivityContext), tags: [new("kora.request.id", request.RequestId.Value)]);
        try
        {
            var result = await reader.ReadAsync(proposed, cutoff, request, cancellation.Token)
                .AsTask().WaitAsync(SessionRetentionPlanner.ScanTimeout, time, cancellation.Token).ConfigureAwait(false);
            lock (gate)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (disposed || requestRevision != revision || !Current())
                {
                    throw new InvalidOperationException("Retention planner admission changed before publication.");
                }
                SessionRetentionPlanner.Validate(result);
                if (result.Snapshot is not null && !reader.IsCurrent(result))
                {
                    throw new InvalidDataException("The exact retention source snapshot changed before publication.");
                }
                if (!Current())
                {
                    throw new InvalidOperationException("Retention admission changed while checking the source receipt.");
                }
                cancellation.Token.ThrowIfCancellationRequested();
                var expires = time.GetUtcNow().Add(SessionRetentionPlanner.PreviewLifetime);
                state = state with
                {
                    Status = result.Status switch
                    {
                        SessionRetentionPlanStatus.Complete => "Complete bounded metadata observation; hypothetical only. Recorded deadlines are unchanged.",
                        SessionRetentionPlanStatus.Incomplete => "Incomplete: observation budget exhausted. No counts or complete-inventory claim.",
                        _ => "Unavailable: source metadata could not be trusted. No counts.",
                    },
                    Result = result,
                    ExpiresUtc = expires,
                };
                expiry = time.CreateTimer(_ => Expire(requestRevision), null,
                    SessionRetentionPlanner.PreviewLifetime, Timeout.InfiniteTimeSpan);
            }
            activity?.SetStatus(result.Status == SessionRetentionPlanStatus.Unavailable
                ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            lock (gate)
            {
                if (requestRevision == revision) { RetireLocked("Preview unavailable: failed, cancelled or timed out. Explicitly Preview again."); }
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
            RetireLocked("Preview expired. Explicitly Preview again.");
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
        observed = null;
        state = new(reason);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) { return; }
            disposed = true;
            RetireLocked("Retention preview closed.");
            reader.Changed -= OnChanged;
            configuration.Changed -= OnChanged;
        }
    }
}

public sealed record SessionRetentionPlannerState(string Status, SessionRetentionSettings? Proposed = null,
    DateTimeOffset? CutoffUtc = null, SessionRetentionConfigurationObservation? Preference = null,
    SessionRetentionPlanResult? Result = null, DateTimeOffset? ExpiresUtc = null,
    SessionRetentionPlannerAdmission? Admission = null);

public sealed record SessionRetentionPlannerAdmission(long ControlRevision, long PrivateRevision, long OwnerRevision, long CallRevision);
