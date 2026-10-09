namespace Kora.Core.Interaction;

public sealed record LocalEventView(LocalEvent Event, LocalEventReason Reason, DateTimeOffset? DeferredUntil)
{
    public string DisplaySummary => Reason == LocalEventReason.Eligible
        ? Event.Summary : "No new notification: " + Reason + ". Exact passive review remains separate.";
}
