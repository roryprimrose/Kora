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
    private string eventUnavailable = "Local event broker unavailable/not observed. No source is inferred; maintenance network checks are separate.";
    public IReadOnlyList<LocalEventView> LocalEvents => eventSnapshot?.Events ?? [];
    public LocalEventView? SelectedLocalEvent => selectedEvent;
    public string LocalEventStatus => eventSnapshot is { } snapshot
        ? "Local event broker: " + snapshot.Reason + "; " + snapshot.Omitted.ToString(CultureInfo.InvariantCulture)
            + " omitted. Eligible is first visual delivery; PresentedNoReplay is passive status only. " + LocalEventSnapshot.Scope
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
        if (!access.CanControl)
        {
            ClearLocalEvents();
            return;
        }
        LocalEventSnapshot observed;
        try
        {
            observed = await localEvents.ObserveAsync(session,
                () => !closed && selectionEpoch == epoch && selected?.Authority.SessionId == session, lifetime.Token);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or OperationCanceledException)
        {
            if (closed || selectionEpoch != epoch || selected?.Authority.SessionId != session) { return; }
            ClearLocalEvents();
            eventUnavailable = "Local event broker held/unavailable: " + exception.GetType().Name
                + ". The independent authoritative work surface remains available; no source, success, replay or default is inferred.";
            NotifyLocalEvents();
            return;
        }
        if (closed || selectionEpoch != epoch || selected?.Authority.SessionId != session) { return; }
        eventSnapshot = observed;
        selectedEvent = null;
        NotifyLocalEvents();
    }

    public Task ReviewLocalEventAsync() => RunLocalEventAsync(LocalEventOperation.Review);
    public Task DismissLocalEventAsync() => RunLocalEventAsync(LocalEventOperation.Dismiss);
    public Task DeferLocalEventAsync() => RunLocalEventAsync(LocalEventOperation.Defer);

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
        }
        finally { presentingEvents = false; }
    }
}
