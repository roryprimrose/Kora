using System.Diagnostics;
using System.Globalization;
using Kora.Application.Infrastructure;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Maintenance;

public sealed partial class MaintenanceViewModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan Freshness = TimeSpan.FromHours(6);
    private readonly IReleaseMetadataClient client;
    private readonly IApplicationInfo info;
    private readonly ReleaseArchitecture architecture;
    private readonly ICanonicalReleasePageOpener opener;
    private readonly TimeProvider time;
    private readonly ISecurityAuditLog audit;
    private readonly ILogger<MaintenanceViewModel> logger;
    private readonly IUiDispatcher dispatcher;
    private readonly Lock cachedGate = new();
    private readonly Func<int> jitterMinutes;
    private readonly ITimer timer;
    private Func<bool> admitted = static () => false;
    private CancellationTokenSource? operation;
    private ReleaseCheck? result;
    private ReleaseCheck? lastVerified;
    private ReleaseCheck? reviewed;
    private ReleaseChannel channel;
    private DateTimeOffset? nextCheck;
    private DateTimeOffset? snoozedUntil;
    private ReleaseVersion? snoozedVersion;
    private ActivityContext scheduledCause;
    private int failures;
    private long revision;
    private bool networkEnabled;
    private bool busy;
    private bool disposed;
    private bool cachedMutationPending;
    private string message = "Unknown: not checked. Maintenance network access is off for this run.";
    private IMaintenanceCommands? cachedCommands;
    private Func<bool> nativeCachedAdmission = static () => true;

    internal sealed record CachedTarget(MaintenanceViewModel Owner, long Revision,
        ReleaseChannel Channel, ReleaseCheck? Result, ReleaseCheck? Reviewed, bool Stale);

    internal CachedTarget CaptureCachedTarget()
    {
        lock (cachedGate) { return new(this, revision, channel, result, reviewed, IsStale); }
    }

    internal bool IsCachedTargetCurrent(CachedTarget target, MaintenanceCommand command)
    {
        lock (cachedGate)
        {
            return !disposed && !busy && admitted() && ReferenceEquals(target.Owner, this)
                && target.Revision == revision && target.Channel == channel && ReferenceEquals(target.Result, result)
                && target.Stale == IsStale
                && ReferenceEquals(command == MaintenanceCommand.Review ? target.Result : target.Reviewed, reviewed);
        }
    }

    public void BindCachedCommands(IMaintenanceCommands commands, Func<bool>? eligible = null)
    {
        cachedCommands = commands;
        if (eligible is not null) { nativeCachedAdmission = eligible; }
    }

    internal string ApplyCached(MaintenanceCommand command, CachedTarget target, HostRequest request,
        Func<bool> eligible, CancellationToken token)
    {
        lock (cachedGate) { return ApplyCachedCore(command, target, request, eligible, token); }
    }

    private string ApplyCachedCore(MaintenanceCommand command, CachedTarget target, HostRequest request,
        Func<bool> eligible, CancellationToken token)
    {
        bool Current() => !disposed && !busy && admitted() && eligible()
            && ReferenceEquals(target.Owner, this) && target.Revision == revision && target.Channel == channel
            && ReferenceEquals(target.Result, result) && ReferenceEquals(target.Reviewed, reviewed)
            && target.Stale == IsStale
            && ReferenceEquals(HostActivity.RequireCurrent().Request, request);
        token.ThrowIfCancellationRequested();
        if (!Current()) { throw new InvalidOperationException("Cached maintenance target or admission changed. Initiate a fresh command."); }
        if (command == MaintenanceCommand.Status)
        {
            return MaintenanceCommandParser.BoundOutput("Cached maintenance: " + channel + "; cached observation " + Availability + "; " + Status + "\n" + VerificationStatus
                + "\nReview ready: " + CanReview + "; snooze ready: " + CanSnooze + ".\n" + Disclosure);
        }
        if (command is not (MaintenanceCommand.Review or MaintenanceCommand.Snooze))
        {
            throw new InvalidOperationException("Only exact cached maintenance operations are admitted.");
        }
        var record = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
            command == MaintenanceCommand.Review ? "maintenance.review" : "maintenance.snooze",
            SecurityAuditOutcome.Requested, request.Origin == RequestOrigin.ActivatedVoice
                ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser, "maintenance.current-run");
        using var activity = HostActivity.BeginAudit(request, record);
        var requested = false;
        var terminal = false;
        try
        {
            audit.Write(record);
            requested = true;
            if (!CanReview || command == MaintenanceCommand.Snooze && !CanSnooze)
            {
                audit.Write(record.WithOutcome(SecurityAuditOutcome.Denied, "stale-or-not-reviewed-notice"));
                terminal = true;
                throw new InvalidOperationException("Fresh exact available metadata and its native review are required to snooze; no other prompt was changed.");
            }
            var output = MaintenanceCommandParser.BoundOutput(channel + "; " + VerificationStatus
                + "\nCanonical cached record: " + result!.Release!.Id.ToString(CultureInfo.InvariantCulture)
                + "; attempted " + result.AttemptedAt.ToString("u", CultureInfo.InvariantCulture) + ".\n" + ReleaseDetails
                + "\n" + ReleasePage + "\n" + Disclosure);
            var proposedMessage = command == MaintenanceCommand.Review
                ? "Native review bound to this exact verified snapshot. Any refresh, expiry or admission change invalidates navigation and snooze."
                : "Reminder snoozed for this exact release for 24 hours in this run. No approval or security prompt changed.";
            var response = MaintenanceCommandParser.BoundOutput(proposedMessage + "\n" + output);
            token.ThrowIfCancellationRequested();
            if (!Current()) { throw new InvalidOperationException("Maintenance target changed before commit."); }
            var priorMessage = message;
            var priorVersion = snoozedVersion;
            var priorUntil = snoozedUntil;
            var committed = false;
            try
            {
                cachedMutationPending = true;
                if (command == MaintenanceCommand.Review) { reviewed = result; }
                else
                {
                    snoozedVersion = result.Release.Version;
                    snoozedUntil = time.GetUtcNow().AddHours(24);
                }
                message = proposedMessage;
                audit.Write(record.WithOutcome(SecurityAuditOutcome.Succeeded));
                terminal = true;
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    snoozedVersion = priorVersion;
                    snoozedUntil = priorUntil;
                    if (revision == target.Revision)
                    {
                        reviewed = target.Reviewed;
                        message = priorMessage;
                    }
                }
                cachedMutationPending = false;
            }
            activity.Complete(HostOperationOutcome.Completed);
            dispatcher.Post(() => { if (!disposed) { Notify(); } });
            return response;
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException
            or UnauthorizedAccessException or OperationCanceledException)
        {
            var cancelled = exception is OperationCanceledException;
            activity.Complete(cancelled ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            if (requested && !terminal)
            {
                audit.Write(record.WithOutcome(cancelled ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed,
                    cancelled ? "cached-operation-cancelled" : "cached-operation-failed"));
            }
            throw;
        }
    }

    public MaintenanceViewModel(IReleaseMetadataClient client, IApplicationInfo info, ReleaseArchitecture architecture,
        ICanonicalReleasePageOpener opener, TimeProvider time, IUiDispatcher dispatcher, ISecurityAuditLog audit,
        ILogger<MaintenanceViewModel> logger, Func<int>? jitterMinutes = null)
    {
        this.client = client;
        this.info = info;
        this.architecture = architecture;
        this.opener = opener;
        this.time = time;
        this.audit = audit;
        this.logger = logger;
        this.dispatcher = dispatcher;
        this.jitterMinutes = jitterMinutes ?? (() => Random.Shared.Next(0, 31));
        timer = time.CreateTimer(_ => dispatcher.Post(OnTimer), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        CheckCommand = new(CheckAsync, ReportFailure);
        ReviewCommand = new(ReviewAsync, ReportFailure);
        OpenCommand = new(OpenAsync, ReportFailure);
        SnoozeCommand = new(SnoozeAsync, ReportFailure);
    }

    public static IReadOnlyList<ReleaseChannel> Channels { get; } = Enum.GetValues<ReleaseChannel>();
    public AsyncCommand CheckCommand { get; }
    public AsyncCommand ReviewCommand { get; }
    public AsyncCommand OpenCommand { get; }
    public AsyncCommand SnoozeCommand { get; }
    public string CurrentVersion => info.Version;
    public string Disclosure => CanonicalRelease.Limits + " " + CanonicalRelease.Runtime;
    public string Status => cachedMutationPending ? "Unknown: cached maintenance audit commit is pending; no success is confirmed." : message;
    public ReleaseAvailability Availability => result?.Status ?? ReleaseAvailability.Unknown;
    public ReleaseCheck? LastVerified => lastVerified;
    public DateTimeOffset? NextCheck => nextCheck;
    public bool IsStale => lastVerified is null || time.GetUtcNow() - lastVerified.VerifiedAt!.Value >= Freshness
        || result?.VerifiedAt is null;
    public string VerificationStatus => lastVerified is { } verified
        ? "Last verified: " + verified.VerifiedAt!.Value.ToString("u", CultureInfo.InvariantCulture) + (IsStale ? " (stale; not current verification)." : ".")
        : "No successful metadata verification.";
    public string ReleasePage => result?.Release?.Page.AbsoluteUri ?? string.Empty;
    public string ReleaseDetails => result?.Release is { } release
        ? $"Version {release.Version.ToString()}; source {release.SourceRevision}; {release.ArtifactName}.\n"
            + release.ArchitectureDisclosure + "\nExpected SHA-256: "
            + release.Assets.Single(asset => string.Equals(asset.Name, release.ArtifactName, StringComparison.Ordinal)).Sha256
        : "No currently verified release is available for review.";
    public bool CanReview => !disposed && !busy && !cachedMutationPending && admitted() && result?.Release is not null && !IsStale;
    public bool CanOpen => CanReview && ReferenceEquals(result, reviewed);
    public bool CanSnooze => CanOpen && result!.Status == ReleaseAvailability.Available;

    public ReleaseChannel Channel
    {
        get => channel;
        set
        {
            if (value == channel) { return; }
            if (!Enum.IsDefined(value)) { throw new InvalidDataException("Unknown release channel."); }
            Mutate("maintenance.channel", () =>
            {
                Invalidate();
                channel = value;
                result = null;
                lastVerified = null;
                message = "Unknown: selected channel changed. Start a new explicit check.";
            });
        }
    }

    public bool NetworkEnabled
    {
        get => networkEnabled;
        set
        {
            if (value == networkEnabled) { return; }
            Mutate("maintenance.network", () =>
            {
                Invalidate();
                networkEnabled = value;
                nextCheck ??= time.GetUtcNow();
                scheduledCause = Activity.Current?.Context ?? default;
                message = value ? "Maintenance metadata network access enabled for this run; initial check scheduled."
                    : "Unknown: maintenance network disabled. Previous verification is historical only.";
                if (!value) { result = null; }
                Schedule();
            });
        }
    }

    public void BindGate(Func<bool> gate) => admitted = gate;

    public void PrivacyClosed()
    {
        lock (cachedGate) { PrivacyClosedCore(); }
    }

    private void PrivacyClosedCore()
    {
        if (disposed) { return; }
        Invalidate();
        result = null;
        message = "Unknown: host/privacy/call admission closed. No release page opened or check replayed.";
        Notify();
    }

    public Task CheckAsync() => RunCheckAsync(automatic: false);

    private async Task RunCheckAsync(bool automatic)
    {
        if (disposed || busy) { return; }
        var origin = automatic ? RequestOrigin.HostSystem : OriginalOrigin;
        using var activity = HostActivity.BeginRoot(HostRequest.Create(origin),
            HostActivityLayer.Application, HostOperation.Runtime,
            automatic && scheduledCause != default ? [new ActivityLink(scheduledCause)] : null);
        if (!admitted() || !networkEnabled || (!automatic && origin != RequestOrigin.LocalUi))
        {
            message = "Unknown: enable maintenance network access through native UI with current host/privacy/call admission.";
            activity.Complete(HostOperationOutcome.Failed);
            Notify();
            return;
        }
        var now = time.GetUtcNow();
        if (nextCheck is { } next && next > now)
        {
            message = "Check deferred until " + next.ToString("u", CultureInfo.InvariantCulture)
                + "; bounded cadence/backoff cannot be bypassed.";
            activity.Complete(HostOperationOutcome.Failed);
            Notify();
            return;
        }
        busy = true;
        operation = new();
        var token = operation.Token;
        var admittedRevision = revision;
        Notify();
        try
        {
            var checkedRelease = await client.CheckAsync(channel, info.Version, architecture, token);
            lock (cachedGate)
            {
                if (disposed || admittedRevision != revision || !admitted() || token.IsCancellationRequested)
                {
                    activity.Complete(HostOperationOutcome.Cancelled);
                    return;
                }
                result = checkedRelease.Release is { } metadata
                    ? checkedRelease with { Release = metadata with { Assets = Array.AsReadOnly(metadata.Assets.ToArray()) } }
                    : checkedRelease;
                reviewed = null;
                if (result.VerifiedAt is not null)
                {
                    lastVerified = result;
                    failures = 0;
                    var jitter = jitterMinutes();
                    if (jitter is < 0 or > 30) { throw new InvalidDataException("Maintenance jitter exceeds its 0-30 minute bound."); }
                    nextCheck = time.GetUtcNow() + Freshness + TimeSpan.FromMinutes(jitter);
                }
                else
                {
                    failures = Math.Min(failures + 1, 8);
                    nextCheck = time.GetUtcNow() + TimeSpan.FromMinutes(Math.Min(360, 5 * (1 << (failures - 1))));
                    if (result.RetryAt is { } retry && retry > nextCheck!.Value) { nextCheck = retry; }
                }
                message = result.Status + ": " + result.Reason;
                if (result.Release?.Version == snoozedVersion && snoozedUntil > time.GetUtcNow())
                {
                    message += " Reminder snoozed for this version in this run.";
                }
                activity.Complete(result.VerifiedAt is not null ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
                Schedule();
            }
        }
        catch (OperationCanceledException)
        {
            if (!disposed) { message = "Unknown: check cancelled; no late metadata presented."; }
            activity.Complete(HostOperationOutcome.Cancelled);
        }
        finally
        {
            operation.Dispose();
            operation = null;
            busy = false;
            if (!disposed) { Notify(); }
        }
    }

    public async Task OpenAsync()
    {
        var origin = OriginalOrigin;
        using var activity = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Presentation);
        var record = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
            "maintenance.open-release", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "maintenance.canonical-release");
        using var auditActivity = HostActivity.BeginAudit(activity.Request, record);
        audit.Write(record);
        if (!CanOpen || origin != RequestOrigin.LocalUi)
        {
            audit.Write(record.WithOutcome(SecurityAuditOutcome.Denied, "stale-or-not-admitted"));
            message = "Release navigation denied: start a fresh native check/review with current ownership/privacy/call admission.";
            auditActivity.Complete(HostOperationOutcome.Failed);
            activity.Complete(HostOperationOutcome.Failed);
            Notify();
            return;
        }
        var version = result!.Release!.Version;
        busy = true;
        operation = new();
        var token = operation.Token;
        var admittedRevision = revision;
        Notify();
        try
        {
            await opener.OpenAsync(version, token);
            if (disposed || admittedRevision != revision || !admitted() || token.IsCancellationRequested)
            {
                audit.Write(record.WithOutcome(SecurityAuditOutcome.Unknown, "navigation-outcome-unknown"));
                if (!disposed)
                {
                    result = null;
                    message = "Unknown: browser navigation admission changed; no late outcome or rollback is claimed.";
                }
                auditActivity.Complete(HostOperationOutcome.Unknown);
                activity.Complete(HostOperationOutcome.Unknown);
                return;
            }
            audit.Write(record.WithOutcome(SecurityAuditOutcome.Succeeded));
            message = "Canonical release page requested in the browser. No replacement artifact downloaded or installed by Kora.";
            auditActivity.Complete(HostOperationOutcome.Completed);
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            audit.Write(record.WithOutcome(SecurityAuditOutcome.Unknown, "navigation-outcome-unknown"));
            if (!disposed)
            {
                result = null;
                message = "Unknown: browser navigation was interrupted; no rollback or cancellation of browser effects is claimed.";
            }
            auditActivity.Complete(HostOperationOutcome.Unknown);
            activity.Complete(HostOperationOutcome.Unknown);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            audit.Write(record.WithOutcome(SecurityAuditOutcome.Failed, "browser-request-failed"));
            ReportFailure(exception);
            auditActivity.Complete(HostOperationOutcome.Failed);
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally
        {
            operation.Dispose();
            operation = null;
            busy = false;
            if (!disposed) { Notify(); }
        }
    }

    public Task ReviewAsync() => ExecuteCachedNativeAsync(MaintenanceCommand.Review);
    public Task SnoozeAsync() => ExecuteCachedNativeAsync(MaintenanceCommand.Snooze);

    private async Task ExecuteCachedNativeAsync(MaintenanceCommand command)
    {
        try
        {
            if (cachedCommands is null) { throw new InvalidOperationException("Durable cached maintenance admission is unavailable."); }
            await cachedCommands.ExecuteAsync(command, OriginalOrigin,
                () => admitted() && nativeCachedAdmission(), CancellationToken.None);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException
            or UnauthorizedAccessException or OperationCanceledException)
        {
            ReportFailure(exception);
        }
    }

    private static RequestOrigin OriginalOrigin => HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi;

    private void Mutate(string action, Action change)
    {
        lock (cachedGate) { MutateCore(action, change); }
    }

    private void MutateCore(string action, Action change)
    {
        var origin = OriginalOrigin;
        using var activity = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Policy);
        var record = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
            action, SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "maintenance.current-run");
        using var auditActivity = HostActivity.BeginAudit(activity.Request, record);
        audit.Write(record);
        if (disposed || !admitted() || origin != RequestOrigin.LocalUi)
        {
            audit.Write(record.WithOutcome(SecurityAuditOutcome.Denied, "host-or-origin-denied"));
            message = "Maintenance change denied: only newly initiated native UI with current host/privacy/call admission is permitted.";
            auditActivity.Complete(HostOperationOutcome.Failed);
            activity.Complete(HostOperationOutcome.Failed);
            Notify();
            return;
        }
        try
        {
            change();
            audit.Write(record.WithOutcome(SecurityAuditOutcome.Succeeded));
            auditActivity.Complete(HostOperationOutcome.Completed);
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException)
        {
            audit.Write(record.WithOutcome(SecurityAuditOutcome.Failed, "invalid-maintenance-state"));
            ReportFailure(exception);
            auditActivity.Complete(HostOperationOutcome.Failed);
            activity.Complete(HostOperationOutcome.Failed);
        }
        Notify();
    }

    private void Schedule()
    {
        if (!networkEnabled) { return; }
        var now = time.GetUtcNow();
        var due = nextCheck!.Value;
        if (lastVerified is { } verified)
        {
            var staleAt = verified.VerifiedAt!.Value + Freshness;
            if (staleAt > now && staleAt < due) { due = staleAt; }
        }
        var delay = due - now;
        timer.Change(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    private void OnTimer()
    {
        if (disposed || !networkEnabled) { return; }
        if (!admitted())
        {
            message = "Unknown: scheduled metadata check deferred by host/privacy/call admission. Use an explicit check after recovery.";
            Notify();
            return;
        }
        if (nextCheck!.Value > time.GetUtcNow())
        {
            Notify();
            Schedule();
            return;
        }
        var command = new AsyncCommand(() => RunCheckAsync(automatic: true), ReportFailure);
        command.Execute(null);
    }

    private void Invalidate()
    {
        revision++;
        operation?.Cancel();
        timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        lock (cachedGate) { DisposeCore(); }
    }

    private void DisposeCore()
    {
        if (disposed) { return; }
        Invalidate();
        disposed = true;
        timer.Dispose();
        admitted = static () => false;
        result = null;
        reviewed = null;
    }

    private void ReportFailure(Exception exception)
    {
        if (disposed) { return; }
        result = null;
        message = "Unknown: maintenance operation failed: " + exception.GetType().Name + ". Start a fresh native review; no success claimed.";
        Failure(logger, exception.GetType().Name);
        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Channel));
        OnPropertyChanged(nameof(NetworkEnabled));
        OnPropertyChanged(nameof(Availability));
        OnPropertyChanged(nameof(NextCheck));
        OnPropertyChanged(nameof(LastVerified));
        OnPropertyChanged(nameof(IsStale));
        OnPropertyChanged(nameof(VerificationStatus));
        OnPropertyChanged(nameof(ReleasePage));
        OnPropertyChanged(nameof(ReleaseDetails));
        OnPropertyChanged(nameof(CanOpen));
        OnPropertyChanged(nameof(CanSnooze));
        OnPropertyChanged(nameof(CanReview));
    }
}
