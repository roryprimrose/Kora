using System.Globalization;
using System.Text.Json;

using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private LocalEventSnapshot? eventSnapshot;
    private LocalEventView? selectedEvent;
    private bool presentingEvents;
    private long eventChoiceEpoch;
    private bool changingRoutineQuiet;
    private volatile bool routineSurfaceOpen;
    private long routineSurfaceRevision;
    private string eventUnavailable = "Local event broker unavailable/not observed. No source is inferred; maintenance network checks are separate.";
    public IReadOnlyList<LocalEventView> LocalEvents => eventSnapshot?.Events ?? [];
    public LocalEventView? SelectedLocalEvent => selectedEvent;
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
    public string LocalEventStatus => eventSnapshot is { } snapshot
        ? "Local event broker: " + snapshot.Reason + "; " + snapshot.Omitted.ToString(CultureInfo.InvariantCulture)
            + " omitted, including " + snapshot.RoutineOmitted.ToString(CultureInfo.InvariantCulture)
            + " routine rows hidden by quiet; " + snapshot.RoutineSuppressed.ToString(CultureInfo.InvariantCulture)
            + " current routine sources suppressed without presentation/budget consumption (RoutineSuppressedNoReplay). "
            + "Eligible is first visual delivery; PresentedNoReplay is passive status only. " + LocalEventSnapshot.Scope
        : eventUnavailable;
    public bool CanControlLocalEvent => CanRead && access.CanControl && selectedEvent is not null;

    public void SelectLocalEvent(LocalEventView? record)
    {
        if (closed || presentingEvents || record is not null && !LocalEvents.Contains(record)) { return; }
        selectedEvent = record;
        OnPropertyChanged(nameof(SelectedLocalEvent));
        OnPropertyChanged(nameof(CanControlLocalEvent));
    }

    private async Task RefreshLocalEventsAsync(HostId<SessionIdentity> session, long epoch)
    {
        if (localEvents is null) { return; }
        var choiceEpoch = eventChoiceEpoch;
        if (!access.CanControl)
        {
            ClearLocalEvents();
            return;
        }
        LocalEventSnapshot observed;
        try
        {
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
        if (closed || selectionEpoch != epoch || eventChoiceEpoch != choiceEpoch
            || selected?.Authority.SessionId != session || !localEvents.IsCurrent(observed)) { return; }
        eventSnapshot = observed;
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
        return RunAsync(async () =>
        {
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
        var target = selectedEvent ?? throw new InvalidOperationException("Select an exact current host event.");
        var epoch = selectionEpoch;
        var result = await (localEvents ?? throw new InvalidOperationException("The local broker is unavailable."))
            .ExecuteNativeAsync(new(operation, target.Event.Id, target.Event.Revision),
                () => !closed && selectionEpoch == epoch && selectedEvent == target
                    && selected?.Authority.SessionId == target.Event.SessionId, lifetime.Token);
        detail = JsonSerializer.Serialize(result);
        selectedEvent = result;
        eventSnapshot = eventSnapshot! with { Events = LocalEvents.Select(item => item.Event.Id == result.Event.Id ? result : item).ToArray() };
        status = "Exact local event " + operation + ": " + result.Reason + ". No question, output policy, maintenance or work authority changed.";
        NotifyLocalEvents();
    });

    private void ClearLocalEvents()
    {
        eventSnapshot = null;
        selectedEvent = null;
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
            OnPropertyChanged(nameof(CanControlLocalEvent));
            OnPropertyChanged(nameof(RoutineQuietStatus));
            OnPropertyChanged(nameof(CanChangeRoutineQuiet));
        }
        finally { presentingEvents = false; }
    }
}
