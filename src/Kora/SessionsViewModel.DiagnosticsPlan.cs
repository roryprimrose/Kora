using System.Globalization;
using System.Text;

using Kora.Application.Hosting;
using Kora.Core.Hosting;
using Kora.Core.Diagnostics;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private readonly HashSet<SessionQueueReadProfile> diagnosticsProfiles = [];
    private SessionDiagnosticsPlanReview? diagnosticsPlanReview;
    private SessionDiagnosticsPlanOutcome? diagnosticsPlanOutcome;
    private long diagnosticsSelectionEpoch;
    private Func<bool> diagnosticsCommandAdmission = static () => true;

    internal Func<bool> CaptureDiagnosticsNavigationEligibility()
    {
        var navigation = CaptureNavigationEligibility();
        var bufferRevision = Interlocked.Read(ref composerBufferRevision);
        return () => navigation() && Interlocked.Read(ref composerBufferRevision) == bufferRevision;
    }

    public bool DiagnosticsVersion { get => diagnosticsProfiles.Contains(SessionQueueReadProfile.Version); set => SetDiagnosticsProfile(SessionQueueReadProfile.Version, value); }
    public bool DiagnosticsCapabilities { get => diagnosticsProfiles.Contains(SessionQueueReadProfile.Capabilities); set => SetDiagnosticsProfile(SessionQueueReadProfile.Capabilities, value); }
    public bool DiagnosticsReadiness { get => diagnosticsProfiles.Contains(SessionQueueReadProfile.Readiness); set => SetDiagnosticsProfile(SessionQueueReadProfile.Readiness, value); }
    public bool DiagnosticsRuntimes { get => diagnosticsProfiles.Contains(SessionQueueReadProfile.Runtimes); set => SetDiagnosticsProfile(SessionQueueReadProfile.Runtimes, value); }
    public bool CanSelectDiagnosticsProfiles => CanControlQueue;
    public bool CanReviewDiagnosticsPlan => CanSelectDiagnosticsProfiles && diagnosticsProfiles.Count != 0;
    public bool CanConfirmDiagnosticsPlan => CanControlQueue && DiagnosticsReviewCurrent;
    public bool CanDiscardDiagnosticsPlan => CanRead && diagnosticsPlanReview is not null;
    private bool DiagnosticsReviewCurrent => diagnosticsPlanReview is { } review && !closed && access.CanInspect && access.CanControl
        && diagnosticsSelectionEpoch == selectionEpoch && access.ControlRevision == review.AdmissionRevision
        && selected?.Authority.SessionId == review.Queue.SessionId && selected.Authority.Generation == review.Queue.Generation
        && queueSnapshot?.Revision == review.Queue.Revision && queueSnapshot.DispatchControl == review.Queue.DispatchControl
        && diagnosticsCommandAdmission();

    internal async Task<bool> PrepareDiagnosticsPlanAsync(SessionDiagnosticsPlanPreparation preparation,
        Func<bool> admitted, Func<bool> nativeAdmission, CancellationToken token, Func<bool>? sourceAdmission = null)
    {
        if (!CanRead || draftRecoveryCancellation is not null)
        { throw new InvalidOperationException("Sessions is busy or unavailable; finish the current native review and retry."); }
        var prepared = false;
        var epoch = selectionEpoch;
        bool Eligible() => !closed && selectionEpoch == epoch && admitted() && nativeAdmission()
            && (sourceAdmission?.Invoke() ?? true);
        System.Diagnostics.ActivityLink[] causes = [new(preparation.Cause)];
        await RunAsync(async () =>
        {
            if (!Eligible())
            {
                status = "Diagnostics plan unavailable: original source/admission changed. No target or buffer changed.";
                return;
            }
            try
            {
                if (!await ProtectComposerAsync())
                {
                    status = "Diagnostics plan not opened: the original composer draft and selection were kept.";
                    return;
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                ReportComposerCloseFailure(exception);
                status = "Diagnostics plan not opened: the original composer save is unconfirmed. Reconcile the exact draft.";
                return;
            }
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
            try
            {
                await service.ValidateDiagnosticsPlanPreparationAsync(preparation, Eligible, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (!Eligible()) { throw new OperationCanceledException("Original diagnostics source changed."); }
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException
                or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
            {
                Failure(logger, exception.GetType().Name);
                status = "Diagnostics plan unavailable: original source/admission changed. Request a fresh exact plan; no target or buffer changed.";
                return;
            }
            LeaveListSearch();
            ClearSelection();
            sessionFilter = Kora.Core.Storage.SessionListFilter.All;
            sessions = new([preparation.Work.Session], null);
            nameDraft = preparation.Work.Session.Metadata?.Name.Value ?? string.Empty;
            historySessionId = preparation.Review.Queue.SessionId.Value.ToString("D");
            ApplyWork(preparation.Work, preparation.Review.AdmissionRevision);
            diagnosticsProfiles.Clear();
            diagnosticsProfiles.UnionWith(preparation.Review.Profiles);
            diagnosticsPlanOutcome = null;
            diagnosticsSelectionEpoch = selectionEpoch;
            diagnosticsCommandAdmission = () => admitted() && nativeAdmission();
            diagnosticsPlanReview = preparation.Review;
            prepared = true;
            OnPropertyChanged(nameof(SessionFilter));
            status = "Exact command preparation only. Inspect the complete original review; nothing was queued or dispatched.";
        }, activitySession: preparation.Review.Queue.SessionId, activityLinks: causes);
        return prepared && CanConfirmDiagnosticsPlan;
    }

    public string DiagnosticsPlanStatus
    {
        get
        {
            if (closed || !access.CanInspect)
            {
                RetireDiagnosticsReview(clearSelection: true);
                return "Local diagnostics plan unavailable; private review and receipts retired.";
            }
            var text = new StringBuilder().AppendLine(SessionDiagnosticsPlanReview.Disclosure);
            if (diagnosticsPlanReview is { } review && DiagnosticsReviewCurrent)
            {
                var control = review.Queue.DispatchControl!;
                text.Append("REVIEW ONLY. Exact session ").Append(review.Queue.SessionId.Value.ToString("D"))
                    .Append(" | generation ").Append(review.Queue.Generation.Value)
                    .Append(" | private control ").Append(review.AdmissionRevision)
                    .Append(" | queue revision ").Append(review.Queue.Revision)
                    .Append(" | limits revision ").Append(review.ConfigurationRevision)
                    .Append("\nRun ").Append(control.RunId.ToString("D")).Append(" | dispatch control revision ").Append(control.Revision)
                    .Append(" | private control revision ").Append(control.ControlRevision)
                    .Append(" | paused ").Append(control.Paused).Append(" | held ").Append(control.Held)
                    .Append("\nSelected count ").Append(review.Profiles.Length).Append(" | pending capacity ")
                    .Append(review.ExistingPending).Append(" -> ").Append(review.ResultingPending).Append('/').Append(review.Limits.PendingPerSession)
                    .Append(" | free after ").Append(review.Limits.PendingPerSession - review.ResultingPending)
                    .Append(" | execution slots unchanged ").Append(review.Limits.ExecutionSlots)
                    .Append("\nFuture pending lifetime ").Append(review.Limits.PendingLifetimeMinutes)
                    .Append(" minutes per enqueue. Future active budget ").Append(review.Limits.ActiveBudgetMinutes)
                    .AppendLine(" minutes is captured only at later manual dispatch, not reserved by this review.");
                for (var index = 0; index < review.Profiles.Length; index++)
                {
                    text.Append(index + 1).Append(". ").Append(SessionQueueReadProfiles.Describe(review.Profiles[index]))
                        .AppendLine(" | input {} | prerequisite None (explicit plan default; no inherited task).");
                }
                text.AppendLine("Existing original deadlines (unchanged):");
                foreach (var entry in review.Queue.Entries)
                {
                    text.Append(entry.Request.TaskId.Value.ToString("D")).Append(" | ").Append(entry.State)
                        .Append(" | original enqueue ").Append(entry.EnqueuedAt.ToString("O", CultureInfo.InvariantCulture))
                        .Append(" | original pending expiry ").Append(entry.ExpiresAt.ToString("O", CultureInfo.InvariantCulture))
                        .Append(" | original active deadline ").AppendLine(entry.ActiveDeadlineAt?.ToString("O", CultureInfo.InvariantCulture) ?? "not admitted");
                }
                text.AppendLine("Separately confirm Enqueue reviewed diagnostics plan; review alone queued nothing.");
            }
            else if (diagnosticsPlanOutcome is { } outcome)
            {
                text.Append("Review ").Append(outcome.ReviewId.ToString("D")).Append(" | stopped by ")
                    .AppendLine(outcome.StoppedBy?.ToString() ?? "none; all selected items accepted");
                foreach (var item in outcome.Items)
                {
                    text.Append(SessionQueueReadProfiles.Describe(item.Profile)).Append(" | ").Append(item.State);
                    if (item.Request is { } request)
                    {
                        text.Append(" | NEW request ").Append(request.RequestId.Value.ToString("D"))
                            .Append(" | task ").Append(request.TaskId.Value.ToString("D"));
                    }
                    if (item.CommittedEntry is { } entry)
                    {
                        text.Append(" | durable queue revision ").Append(item.CommittedQueueRevision)
                            .Append(" | entry revision ").Append(entry.Revision.Value)
                            .Append(" | original enqueue ").Append(entry.EnqueuedAt.ToString("O", CultureInfo.InvariantCulture))
                            .Append(" | original expiry ").Append(entry.ExpiresAt.ToString("O", CultureInfo.InvariantCulture));
                    }
                    else if (item.Request is not null) { text.Append(" | commit unconfirmed: inspect these exact IDs; do not automatically retry"); }
                    text.AppendLine();
                }
            }
            else
            {
                text.Append("Selected ").Append(diagnosticsProfiles.Count)
                    .AppendLine(" of four fixed profiles. Initial selection is empty. Choose explicitly, then Review.")
                    .AppendLine("Order: Version, Capabilities, Readiness, Runtimes, filtered to your selection. All prerequisites: None.");
            }
            return text.ToString();
        }
    }

    private void SetDiagnosticsProfile(SessionQueueReadProfile profile, bool selectedProfile)
    {
        if (notifying || !CanSelectDiagnosticsProfiles || diagnosticsProfiles.Contains(profile) == selectedProfile) { return; }
        RetireDiagnosticsReview();
        diagnosticsPlanOutcome = null;
        if (selectedProfile) { diagnosticsProfiles.Add(profile); }
        else { diagnosticsProfiles.Remove(profile); }
        NotifyDiagnosticsPlan();
    }

    public Task ReviewDiagnosticsPlanAsync()
    {
        var epoch = selectionEpoch;
        var snapshot = queueSnapshot;
        var profiles = diagnosticsProfiles.OrderBy(profile => profile).ToArray();
        return RunAsync(async () =>
        {
            RetireDiagnosticsReview();
            diagnosticsPlanOutcome = null;
            var target = RequireSelected();
            if (snapshot is null || snapshot.SessionId != target.Authority.SessionId)
            {
                throw new InvalidOperationException("Refresh the exact selected queue before reviewing diagnostics.");
            }
            diagnosticsSelectionEpoch = epoch;
            diagnosticsPlanReview = await service.ReviewDiagnosticsPlanAsync(snapshot, profiles,
                SessionDiagnosticsPlanPrerequisite.None, () => !closed && selectionEpoch == epoch
                    && selected?.Authority == target.Authority, lifetime.Token);
            status = "Review only: no queue writes. Inspect complete plan, None prerequisites, capacity and deadlines before separate confirmation.";
        });
    }

    public Task ConfirmDiagnosticsPlanAsync() => RunAsync(async () =>
    {
        var review = diagnosticsPlanReview ?? throw new InvalidOperationException("Review your exact selected profiles first.");
        if (!DiagnosticsReviewCurrent) { RetireDiagnosticsReview(); throw new InvalidOperationException("Diagnostics review is stale."); }
        diagnosticsPlanReview = null;
        var commandAdmission = diagnosticsCommandAdmission;
        diagnosticsCommandAdmission = static () => true;
        var epoch = selectionEpoch;
        var outcome = await service.ConfirmDiagnosticsPlanAsync(review, () => !closed && selectionEpoch == epoch
            && selected?.Authority.SessionId == review.Queue.SessionId && commandAdmission(), lifetime.Token);
        // Keep exact partial/uncertain receipts even when the follow-up passive observation is unavailable.
        diagnosticsPlanOutcome = outcome;
        status = "Sequential diagnostics enqueue finished; inspect every item outcome. Earlier commits remain queued. No dispatch or retry.";
        queueSnapshot = null;
        selectedQueueEntry = null;
        InvalidateQueuePrerequisite(requireDecision: true);
    });

    public void DiscardDiagnosticsPlan()
    {
        if (!CanRead) { return; }
        RetireDiagnosticsReview();
        NotifyDiagnosticsPlan();
    }

    private void RetireDiagnosticsReview(bool clearSelection = false)
    {
        if (diagnosticsPlanReview is { } review) { service.RetireDiagnosticsPlan(review); }
        diagnosticsPlanReview = null;
        diagnosticsCommandAdmission = static () => true;
        if (clearSelection) { diagnosticsProfiles.Clear(); diagnosticsPlanOutcome = null; }
    }

    private void NotifyDiagnosticsPlan()
    {
        NotifyPreparedQueuePrerequisite();
        if (closed || !access.CanInspect) { RetireDiagnosticsReview(clearSelection: true); }
        else if (diagnosticsPlanReview is not null && !DiagnosticsReviewCurrent) { RetireDiagnosticsReview(); }
        OnPropertyChanged(nameof(DiagnosticsVersion));
        OnPropertyChanged(nameof(DiagnosticsCapabilities));
        OnPropertyChanged(nameof(DiagnosticsReadiness));
        OnPropertyChanged(nameof(DiagnosticsRuntimes));
        OnPropertyChanged(nameof(CanSelectDiagnosticsProfiles));
        OnPropertyChanged(nameof(CanReviewDiagnosticsPlan));
        OnPropertyChanged(nameof(CanConfirmDiagnosticsPlan));
        OnPropertyChanged(nameof(CanDiscardDiagnosticsPlan));
        OnPropertyChanged(nameof(DiagnosticsPlanStatus));
    }
}
