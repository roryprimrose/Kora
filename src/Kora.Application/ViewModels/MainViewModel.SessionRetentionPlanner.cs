using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly SessionRetentionPlannerService? sessionRetentionPlanner;
    private int? proposedSessionArchiveDays;
    private int? proposedSessionDeleteDays;
    private long sessionRetentionPlanControlRevision;
    private Func<bool>? sessionRetentionPlanEligibility;

    public AsyncCommand PreviewSessionRetentionCommand { get; }
    public int? ProposedSessionArchiveDays
    {
        get => proposedSessionArchiveDays;
        set
        {
            if (SetProperty(ref proposedSessionArchiveDays, value)) { RetireEditedSessionRetentionPlan(); }
        }
    }

    public int? ProposedSessionDeleteDays
    {
        get => proposedSessionDeleteDays;
        set
        {
            if (SetProperty(ref proposedSessionDeleteDays, value)) { RetireEditedSessionRetentionPlan(); }
        }
    }

    public bool CanPreviewSessionRetention => sessionRetentionPlanner is not null && CanChangeSessionRetentionNative
        && sessionRetentionConfiguration!.Available && ProposedSessionArchiveDays is >= 1
        && ProposedSessionDeleteDays is <= SessionRetentionSettings.MaximumDays
        && ProposedSessionArchiveDays < ProposedSessionDeleteDays;

    public string SessionRetentionPlanStatus
    {
        get
        {
            var preview = sessionRetentionPlanner?.Get();
            if (preview is null) { return "Session retention planner unavailable. No policy or session changes."; }
            var text = preview.Status;
            if (preview.Proposed is { } proposed)
            {
                text += $" Proposed limits: archive after {proposed.ArchiveDays} inactive days; delete after {proposed.DeleteDays}."
                    + " This preview expires after 30 seconds.";
            }
            if (preview.Preference is { } preference)
            {
                text += $" Current limits: archive after {preference.Effective.ArchiveDays} inactive days; delete after {preference.Effective.DeleteDays}.";
            }
            if (preview.Result is { } result)
            {
                text += $" Results: {result.Status}.";
                if (result.Aggregate is { } counts)
                {
                    text += $" Sessions checked: {counts.Sessions}. Newly due with these limits:"
                        + $" archive {counts.NewlyArchiveDue}; delete {counts.NewlyDeleteDue}. Already due under existing dates:"
                        + $" archive {counts.RecordedArchiveDue}; delete {counts.RecordedDeleteDue}."
                        + $" Not counted: marked to keep {counts.Kept}; ongoing work {counts.WorkHeld};"
                        + $" unknown work status {counts.UnknownHold}; already removed {counts.Removed}."
                        + " These are estimates, not a promise that sessions can be archived or deleted.";
                }
                else { text += " No count is available. This does not mean zero sessions are affected."; }
            }
            return text;
        }
    }

    private void RetireEditedSessionRetentionPlan()
    {
        Interlocked.Increment(ref sessionRetentionPlanControlRevision);
        sessionRetentionPlanner?.Retire("Proposed policy edited; preview retired. Explicitly Preview again.");
        NotifySessionRetentionPlan();
    }

    private void OnSessionRetentionPlannerChanged(object? sender, EventArgs args) => uiDispatcher.Post(() =>
    {
        if (!disposed) { NotifySessionRetentionPlan(); }
    });

    private void NotifySessionRetentionPlan()
    {
        OnPropertyChanged(nameof(SessionRetentionPlanStatus));
        OnPropertyChanged(nameof(CanPreviewSessionRetention));
        PreviewSessionRetentionCommand.NotifyCanExecuteChanged();
    }

    private void RetireIneligibleSessionRetentionPlan()
    {
        if (sessionRetentionPlanEligibility is not null && !sessionRetentionPlanEligibility())
        {
            sessionRetentionPlanEligibility = null;
            sessionRetentionPlanner!.Retire("Private, owner or control state changed; preview retired.");
        }
        NotifySessionRetentionPlan();
    }

    private async Task PreviewSessionRetentionAsync()
    {
        if (!CanPreviewSessionRetention) { return; }
        var lifetime = sessionRetentionNativeLifetime;
        var host = CaptureConfigurationControlEligibility(RequestOrigin.LocalUi);
        var proposed = new SessionRetentionSettings(ProposedSessionArchiveDays!.Value, ProposedSessionDeleteDays!.Value);
        var admission = new SessionRetentionPlannerAdmission(Interlocked.Read(ref sessionRetentionPlanControlRevision),
            privacyObservation.Current.TopologyRevision, auditImpactOwnerRevision(), CallPolicyRevision);
        bool Eligible() => host() && ReferenceEquals(lifetime, sessionRetentionNativeLifetime) && lifetime()
            && admission.ControlRevision == Interlocked.Read(ref sessionRetentionPlanControlRevision)
            && admission.PrivateRevision == privacyObservation.Current.TopologyRevision
            && admission.OwnerRevision == auditImpactOwnerRevision() && admission.CallRevision == CallPolicyRevision
            && ProposedSessionArchiveDays == proposed.ArchiveDays && ProposedSessionDeleteDays == proposed.DeleteDays;
        sessionRetentionPlanEligibility = Eligible;
        try { await sessionRetentionPlanner!.PreviewAsync(proposed, admission, Eligible); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or InvalidOperationException or OperationCanceledException or TimeoutException or ArgumentOutOfRangeException)
        {
            ApplicationLog.SessionRetentionPreviewFailed(logger, exception.GetType().Name);
        }
        finally { if (!disposed) { NotifySessionRetentionPlan(); } }
    }
}
