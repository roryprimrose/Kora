using System.Globalization;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private SessionRetentionObservation? retentionObservation;
    private RetentionReview? retentionReview;
    private HostId<SessionIdentity>? retentionSubject;

    private sealed record RetentionReview(SessionRetentionObservation Observation, bool Keep,
        long SelectionEpoch, long ControlRevision);

    public string RetentionStatus => retentionObservation is { } observed
        ? DescribeRetention(observed)
        : "Retention unavailable/not yet read. Refresh the exact selected session; no default or cleanup is inferred.";

    public string RetentionPreview => retentionReview is { } review
        ? (review.Keep ? "Confirm Keep this session: automatic session archive/delete will be held."
            : "Confirm Use ordinary retention: background maintenance may archive or delete soon, including already-due dates. No clock reset or apply-now cleanup.")
            + "\n" + DescribeRetention(review.Observation)
        : "Keep is a SESSION retention hold, not a Perpetual OPERATION grant, consent, memory, recall or execution permission. Review and confirm one exact ID.";

    public bool CanReadExactRetention => CanRead
        && SessionListSearch.IsValid(SessionListSearchKind.ExactId, SessionListFilter.All, historySessionId);
    public bool CanRefreshRetention => CanRead && (selected is not null || retentionSubject is not null);
    public bool CanKeepSession => CanRefreshRetention && access.CanControl
        && retentionObservation is { State.Perpetual: false, State.Purged: false, Removed: false }
        && RetentionSubjectMatches;
    public bool CanUseOrdinaryRetention => CanRefreshRetention && access.CanControl
        && retentionObservation is { State.Perpetual: true, State.Purged: false, Removed: false }
        && RetentionSubjectMatches;
    public bool CanConfirmRetention => CanRefreshRetention && access.CanControl && retentionReview is { } review
        && review.SelectionEpoch == selectionEpoch && review.ControlRevision == access.ControlRevision
        && ReferenceEquals(review.Observation, retentionObservation)
        && RetentionSubjectMatches;

    public Task RefreshRetentionAsync() => RunAsync(ReadSelectedRetentionAsync);
    public Task ReadExactRetentionAsync() => RunAsync(async () =>
    {
        var query = new SessionListSearch(SessionListSearchKind.ExactId, SessionListFilter.All, historySessionId);
        var id = new HostId<SessionIdentity>(query.ExactId!.Value);
        ClearSelection();
        retentionSubject = id;
        await ReadSelectedRetentionAsync();
        status = "Exact-ID retention metadata only; no name, history, content, resume or work observation was read.";
    });

    private bool RetentionSubjectMatches => retentionObservation is { } observed
        && observed.State.SessionId == retentionSubject
        && (selected is null || selected.Authority.SessionId == retentionSubject
            && selected.Authority.Generation == observed.Generation);

    private async Task ReadSelectedRetentionAsync()
    {
        var subject = selected?.Authority.SessionId ?? retentionSubject
            ?? throw new InvalidOperationException("Select a session or enter its exact ID for retention status.");
        retentionSubject = subject;
        var epoch = selectionEpoch;
        retentionReview = null;
        retentionObservation = null;
        var observed = await service.ReadRetentionAsync(subject, lifetime.Token);
        if (closed || !access.CanInspect || epoch != selectionEpoch
            || retentionSubject != observed.State.SessionId
            || selected is { } target && (target.Authority.SessionId != subject || target.Authority.Generation != observed.Generation))
        {
            throw new OperationCanceledException("Retention selection or generation changed. Refresh before review.");
        }
        retentionObservation = observed;
    }

    public void PreviewRetention(bool keep)
    {
        if (keep ? !CanKeepSession : !CanUseOrdinaryRetention) { return; }
        retentionReview = new(retentionObservation!, keep, selectionEpoch, access.ControlRevision);
        NotifyRetention();
    }

    public void CancelRetentionReview()
    {
        retentionReview = null;
        NotifyRetention();
    }

    public Task ConfirmRetentionAsync() => RunAsync(async () =>
    {
        var review = retentionReview ?? throw new InvalidOperationException("Review the exact retention state first.");
        bool Eligible() => !closed && access.CanControl && selectionEpoch == review.SelectionEpoch
            && access.ControlRevision == review.ControlRevision && ReferenceEquals(retentionReview, review)
            && ReferenceEquals(retentionObservation, review.Observation)
            && RetentionSubjectMatches;
        var observed = await service.SetRetentionHoldAsync(review.Observation, review.Keep,
            RequestOrigin.LocalUi, Eligible, lifetime.Token);
        if (!Eligible()) { throw new OperationCanceledException("Native retention review closed or changed after commit."); }
        retentionReview = null;
        retentionObservation = observed;
        status = "Retention setting, required audit, terminal receipt and exact readback committed. Clock, grants and execution unchanged.";
    }, activitySession: retentionSubject);

    private static string DescribeRetention(SessionRetentionObservation observed)
    {
        var state = observed.State;
        return "Exact session " + state.SessionId.Value.ToString("D")
            + " | generation " + observed.Generation.Value.ToString(CultureInfo.InvariantCulture)
            + " | exemption audit revision " + observed.ExemptionAuditSequence.ToString(CultureInfo.InvariantCulture)
            + "\nObserved UTC: " + observed.ObservedAt.ToString("O", CultureInfo.InvariantCulture)
            + "\nLast meaningful activity UTC: " + state.LastMeaningfulActivity.ToString("O", CultureInfo.InvariantCulture)
            + "\nRecorded archive due UTC: " + state.ArchiveDue.ToString("O", CultureInfo.InvariantCulture)
            + "\nRecorded delete due UTC: " + state.DeleteDue.ToString("O", CultureInfo.InvariantCulture)
            + (state.DeleteDue <= observed.ObservedAt ? " (already due at observation)"
                : state.ArchiveDue <= observed.ObservedAt ? " (archive already due at observation)" : "")
            + "\nKept: " + state.Perpetual + " | purged: " + state.Purged + " | removed: " + observed.Removed
            + (observed.WorkHoldObserved
                ? "\nObserved hold: live/Unknown work, unresolved questions or current-run control authority."
                : "\nNo work hold observed at this read; not a cleanup eligibility guarantee.")
            + "\nCopy inventory and other maintenance availability are not observed by this panel. Live/Unknown/copy holds remain enforced by the private timer.";
    }

    private void ClearRetention()
    {
        retentionObservation = null;
        retentionReview = null;
        retentionSubject = null;
    }

    private void NotifyRetention()
    {
        OnPropertyChanged(nameof(RetentionStatus));
        OnPropertyChanged(nameof(RetentionPreview));
        OnPropertyChanged(nameof(CanRefreshRetention));
        OnPropertyChanged(nameof(CanKeepSession));
        OnPropertyChanged(nameof(CanUseOrdinaryRetention));
        OnPropertyChanged(nameof(CanConfirmRetention));
        OnPropertyChanged(nameof(CanReadExactRetention));
    }
}
