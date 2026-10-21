using System.Globalization;
using System.Text.Json;

using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Diagnostics;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private LocalEventSnapshot? eventSnapshot;
    private LocalEventView? selectedEvent;
    private LocalEventFilter eventFilter = new();
    private long eventFilterEpoch;
    private string? LocalEventFilterDigest => eventFilter.IsValid ? eventFilter.Digest : null;
    private long eventSelectionEpoch;
    private bool eventDetailPresented;
    private Kora.Core.Authorization.WorkSessionAuthorization? eventAuthority;
    private bool presentingEvents;
    private long eventChoiceEpoch;
    private bool changingRoutineQuiet;
    private volatile bool routineSurfaceOpen;
    private long routineSurfaceRevision;
    private string eventUnavailable = "Local event broker unavailable/not observed. No source is inferred; maintenance network checks are separate.";
    private bool EventSourceCurrent => !closed && access.CanInspect && access.CanControl
        && eventSnapshot is { } snapshot && localEvents?.IsCurrent(snapshot) == true
        && selected?.Authority == eventAuthority && selectionEpoch == eventSelectionEpoch;
    public IReadOnlyList<LocalEventView> LocalEvents => EventSourceCurrent
        ? [.. eventSnapshot!.Events.Where(eventFilter.Matches)] : [];
    public LocalEventView? SelectedLocalEvent => EventSourceCurrent ? selectedEvent : null;
    public bool CanFilterLocalEvents => !closed && access.CanInspect && access.CanControl
        && selected is not null && localEvents is { IsAvailable: true };
    public LocalEventCategory? LocalEventCategoryFilter
    {
        get => eventFilter.Category;
        set
        {
            if (presentingEvents || !CanFilterLocalEvents || value == eventFilter.Category) { return; }
            eventFilter = eventFilter with { Category = value };
            ChangeLocalEventFilters();
        }
    }
    public LocalEventReason? LocalEventReasonFilter
    {
        get => eventFilter.Reason;
        set
        {
            if (presentingEvents || !CanFilterLocalEvents || value == eventFilter.Reason) { return; }
            eventFilter = eventFilter with { Reason = value };
            ChangeLocalEventFilters();
        }
    }
    public IReadOnlyList<LocalEventFilterOption<LocalEventCategory>> LocalEventCategoryFilters { get; } =
        [new(null), .. Enum.GetValues<LocalEventCategory>().Select(value => new LocalEventFilterOption<LocalEventCategory>(value))];
    public IReadOnlyList<LocalEventFilterOption<LocalEventReason>> LocalEventReasonFilters { get; } =
        [new(null), .. Enum.GetValues<LocalEventReason>().Select(value => new LocalEventFilterOption<LocalEventReason>(value))];
    public LocalEventFilterOption<LocalEventCategory> LocalEventCategoryChoice
    {
        get => LocalEventCategoryFilters.FirstOrDefault(option => option.Value == eventFilter.Category) ?? new(eventFilter.Category);
        set { if (value is not null) { LocalEventCategoryFilter = value.Value; } }
    }
    public LocalEventFilterOption<LocalEventReason> LocalEventReasonChoice
    {
        get => LocalEventReasonFilters.FirstOrDefault(option => option.Value == eventFilter.Reason) ?? new(eventFilter.Reason);
        set { if (value is not null) { LocalEventReasonFilter = value.Value; } }
    }
    public string LocalEventFilterStatus => !eventFilter.IsValid
        ? "Unknown category or delivery reason; no results. Clear filters to recover."
        : !EventSourceCurrent ? "No current bounded local-event snapshot. Refresh selected work; no empty source is inferred."
        : LocalEvents.Count.ToString(CultureInfo.InvariantCulture) + " of "
            + eventSnapshot!.Events.Count.ToString(CultureInfo.InvariantCulture)
            + " loaded local events match the filters. "
            + (LocalEvents.Count == 0 ? "No matching loaded events. " : string.Empty)
            + "Select an exact visible event separately before Review/Dismiss/Defer. Snapshot omissions and quiet status remain separate.";

    public void ClearLocalEventFilters()
    {
        if (presentingEvents || !CanFilterLocalEvents) { return; }
        eventFilter = new();
        ChangeLocalEventFilters();
    }

    private void ChangeLocalEventFilters()
    {
        RetireLocalEventSelection();
        eventFilterEpoch++;
        NotifyLocalEvents();
    }

    private void RetireLocalEventSelection()
    {
        selectedEvent = null;
        if (eventDetailPresented)
        {
            eventDetailPresented = false;
            Detail = string.Empty;
            OnPropertyChanged(nameof(Detail));
        }
    }

    internal sealed record LocalEventFilterOption<T>(T? Value, string AnyLabel = "All") where T : struct, Enum
    {
        public override string ToString() => Value?.ToString() ?? AnyLabel;
    }
    public string RoutineQuietStatus => localEvents is { } broker
        ? "Quiet routine notices for this run: " + (broker.RoutineQuiet.Enabled ? "On" : "Off")
            + (broker.IsAvailable ? string.Empty : " (held/unavailable; refresh recovery, no confirmed change inferred)")
            + ". Work and Maintenance notice deliveries and rows only; until explicitly cleared or Kora restarts. "
            + "Failures, Attention, required questions/approvals, authoritative work/status and recovery remain unchanged. No speech."
        : "Quiet routine notices unavailable. No choice or source is inferred.";
    public bool CanChangeRoutineQuiet => routineSurfaceOpen && !changingRoutineQuiet && CanRead && access.CanControl
        && selected is not null && localEvents is { IsAvailable: true };

    internal void SetRoutineQuietSurfaceOpen(bool visible)
    {
        if (closed || routineSurfaceOpen == visible) { return; }
        routineSurfaceOpen = visible;
        Interlocked.Increment(ref routineSurfaceRevision);
        if (!visible) { ClearLocalEvents(); }
        else { NotifyLocalEvents(); }
    }
    public string LocalEventStatus => EventSourceCurrent && eventSnapshot is { } snapshot
        ? "Local event broker: " + snapshot.Reason + "; " + snapshot.Omitted.ToString(CultureInfo.InvariantCulture)
            + " omitted, including " + snapshot.RoutineOmitted.ToString(CultureInfo.InvariantCulture)
            + " routine rows hidden by quiet; " + snapshot.RoutineSuppressed.ToString(CultureInfo.InvariantCulture)
            + " current routine sources suppressed without presentation/budget consumption (RoutineSuppressedNoReplay). "
            + "Eligible is first visual delivery; PresentedNoReplay is passive status only. " + LocalEventSnapshot.Scope
        : eventSnapshot is not null ? "Local event snapshot withdrawn after source/admission change. Refresh selected work; no current source is inferred." : eventUnavailable;
    public bool CanControlLocalEvent => CanRead && EventSourceCurrent && selectedEvent is not null
        && LocalEvents.Contains(selectedEvent);

    public void SelectLocalEvent(LocalEventView? record)
    {
        if (closed || presentingEvents || !EventSourceCurrent || record is not null && !LocalEvents.Contains(record)) { return; }
        RetireLocalEventSelection();
        selectedEvent = record;
        eventFilterEpoch++;
        OnPropertyChanged(nameof(SelectedLocalEvent));
        OnPropertyChanged(nameof(CanControlLocalEvent));
    }

    private async Task RefreshLocalEventsAsync(HostId<SessionIdentity> session, long epoch)
    {
        if (localEvents is null) { return; }
        var choiceEpoch = eventChoiceEpoch;
        var filterEpoch = eventFilterEpoch;
        var filterDigest = LocalEventFilterDigest;
        if (!access.CanControl)
        {
            ClearLocalEvents();
            return;
        }
        LocalEventSnapshot observed;
        try
        {
            // The retained source lease is independent of filters; only this pending publication binds their identity.
            observed = await localEvents.ObserveAsync(session,
                () => !closed && selectionEpoch == epoch && eventChoiceEpoch == choiceEpoch
                    && selected?.Authority.SessionId == session, lifetime.Token);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or OperationCanceledException)
        {
            if (closed || selectionEpoch != epoch || eventChoiceEpoch != choiceEpoch || selected?.Authority.SessionId != session) { return; }
            ClearLocalEvents();
            eventUnavailable = "Local event broker held/unavailable: " + exception.GetType().Name
                + ". The independent authoritative work surface remains available; no source, success, replay or default is inferred.";
            NotifyLocalEvents();
            return;
        }
        if (closed || selectionEpoch != epoch || eventChoiceEpoch != choiceEpoch || eventFilterEpoch != filterEpoch
            || !string.Equals(LocalEventFilterDigest, filterDigest, StringComparison.Ordinal)
            || selected?.Authority.SessionId != session || !localEvents.IsCurrent(observed)) { return; }
        eventSnapshot = observed;
        eventAuthority = selected.Authority;
        eventSelectionEpoch = epoch;
        selectedEvent = null;
        NotifyLocalEvents();
    }

    public Task ReviewLocalEventAsync() => RunLocalEventAsync(LocalEventOperation.Review);
    public Task DismissLocalEventAsync() => RunLocalEventAsync(LocalEventOperation.Dismiss);
    public Task DeferLocalEventAsync() => RunLocalEventAsync(LocalEventOperation.Defer);

    public Task QuietRoutineNoticesAsync() => ChangeRoutineQuietAsync(true);
    public Task ClearRoutineQuietAsync() => ChangeRoutineQuietAsync(false);
    public Task ResetRoutineQuietAsync() => ChangeRoutineQuietAsync(false);

    private Task ChangeRoutineQuietAsync(bool enabled)
    {
        var session = selected?.Authority.SessionId;
        var originalAllowed = !HostActivity.HasScope || HostActivity.Current is { } original
            && original.Request.Origin == RequestOrigin.LocalUi && !original.Activity!.IsStopped;
        return RunAsync(async () =>
        {
            if (!originalAllowed) { throw new InvalidOperationException("Quiet control cannot relabel model/system/voice or completed ambient input as native user input."); }
            var target = RequireSelected();
            var broker = localEvents ?? throw new InvalidOperationException("The local broker is unavailable.");
            if (!routineSurfaceOpen) { throw new InvalidOperationException("Run-only quiet control requires the already-open native Sessions surface."); }
            var epoch = selectionEpoch;
            var choice = broker.RoutineQuiet;
            var surfaceRevision = Volatile.Read(ref routineSurfaceRevision);
            ClearLocalEvents();
            var choiceEpoch = ++eventChoiceEpoch;
            await broker.ChangeRoutineQuietNativeAsync(target.Authority.SessionId, enabled, choice.Revision,
                () => !closed && routineSurfaceOpen && Volatile.Read(ref routineSurfaceRevision) == surfaceRevision
                    && access.CanControl && selectionEpoch == epoch && eventChoiceEpoch == choiceEpoch
                    && selected?.Authority == target.Authority, lifetime.Token);
            await RefreshLocalEventsAsync(target.Authority.SessionId, epoch);
            status = "Run-only quiet choice admitted. Clear/reset permits future new notices only; no muted backlog, work, question or output policy changed.";
            NotifyLocalEvents();
        }, activitySession: session, routineNoticeControl: true);
    }

    private Task RunLocalEventAsync(LocalEventOperation operation) => RunAsync(async () =>
    {
        var target = SelectedLocalEvent ?? throw new InvalidOperationException("Select an exact current host event.");
        if (!LocalEvents.Contains(target)) { throw new InvalidOperationException("Select an exact visible host event under the current filters."); }
        var epoch = selectionEpoch;
        var filterEpoch = eventFilterEpoch;
        var filterDigest = LocalEventFilterDigest;
        var result = await (localEvents ?? throw new InvalidOperationException("The local broker is unavailable."))
            .ExecuteNativeAsync(new(operation, target.Event.Id, target.Event.Revision),
                () => !closed && selectionEpoch == epoch && eventFilterEpoch == filterEpoch
                    && string.Equals(LocalEventFilterDigest, filterDigest, StringComparison.Ordinal)
                    && EventSourceCurrent && selectedEvent == target
                    && selected?.Authority.SessionId == target.Event.SessionId, lifetime.Token);
        if (!EventSourceCurrent || selectionEpoch != epoch || eventFilterEpoch != filterEpoch || selectedEvent != target
            || !string.Equals(LocalEventFilterDigest, filterDigest, StringComparison.Ordinal))
        {
            throw new OperationCanceledException("Local event presentation changed; no late selection or details are admitted.");
        }
        if (result.Reason is LocalEventReason.SourceUnavailable or LocalEventReason.AdmissionChanged
            or LocalEventReason.Expired or LocalEventReason.ClockRollback)
        {
            throw new InvalidOperationException("The exact local event source is no longer current: " + result.Reason + ". Refresh and select separately.");
        }
        Detail = JsonSerializer.Serialize(result);
        eventDetailPresented = true;
        eventSnapshot = eventSnapshot! with { Events = eventSnapshot.Events.Select(item => item.Event.Id == result.Event.Id ? result : item).ToArray() };
        selectedEvent = eventFilter.Matches(result) ? result : null;
        if (selectedEvent is null) { RetireLocalEventSelection(); }
        status = "Exact local event " + operation + ": " + result.Reason + ". No question, output policy, maintenance or work authority changed.";
        NotifyLocalEvents();
    });

    private void ClearLocalEvents()
    {
        eventSnapshot = null;
        eventAuthority = null;
        RetireLocalEventSelection();
        eventChoiceEpoch++;
        NotifyLocalEvents();
    }

    private void NotifyLocalEvents()
    {
        presentingEvents = true;
        try
        {
            OnPropertyChanged(nameof(LocalEvents));
            OnPropertyChanged(nameof(SelectedLocalEvent));
            OnPropertyChanged(nameof(LocalEventStatus));
            OnPropertyChanged(nameof(LocalEventCategoryFilter));
            OnPropertyChanged(nameof(LocalEventReasonFilter));
            OnPropertyChanged(nameof(LocalEventCategoryChoice));
            OnPropertyChanged(nameof(LocalEventReasonChoice));
            OnPropertyChanged(nameof(LocalEventFilterStatus));
            OnPropertyChanged(nameof(CanFilterLocalEvents));
            OnPropertyChanged(nameof(CanControlLocalEvent));
            OnPropertyChanged(nameof(RoutineQuietStatus));
            OnPropertyChanged(nameof(CanChangeRoutineQuiet));
        }
        finally { presentingEvents = false; }
    }
}
