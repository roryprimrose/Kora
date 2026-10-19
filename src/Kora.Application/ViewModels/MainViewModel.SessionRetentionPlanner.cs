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
                text += $" Hypothetical policy: archive {proposed.ArchiveDays} / delete {proposed.DeleteDays} idle days."
                    + $" Fixed UTC cutoff: {preview.CutoffUtc:O}. Expires UTC: {preview.ExpiresUtc:O}.";
            }
            if (preview.Preference is { } preference)
            {
                text += $" Future-only current policy: {preference.Effective.ArchiveDays} / {preference.Effective.DeleteDays}."
                    + $" Preference revision {preference.PreferenceRevision}; policy revision {preference.PolicyRevision}.";
            }
            if (preview.Admission is { } admission)
            {
                text += $" Control revision {admission.ControlRevision}; private revision {admission.PrivateRevision};"
                    + $" owner revision {admission.OwnerRevision}; call revision {admission.CallRevision}.";
            }
            if (preview.Result is { } result)
            {
                text += $" Source: {result.Status}.";
                if (result.Snapshot is { } snapshot) { text += $" Exact metadata snapshot: {snapshot}."; }
                if (result.Aggregate is { } counts)
                {
                    text += $" Sessions observed: {counts.Sessions}. Hypothetically newly archive-due: {counts.NewlyArchiveDue};"
                        + $" newly delete-due: {counts.NewlyDeleteDue}. Already due by recorded deadlines (not recomputed):"
                        + $" archive {counts.RecordedArchiveDue}; delete {counts.RecordedDeleteDue}."
                        + $" Exclusions: kept {counts.Kept}; known work-held {counts.WorkHeld};"
                        + $" unknown work hold {counts.UnknownHold}; removed {counts.Removed}."
                        + " Due counts exclude these categories. Unknown holds never imply eligible."
                        + " This is not cleanup eligibility or a storage-removal forecast.";
                }
                else { text += " No aggregate available; do not interpret this as zero."; }
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
