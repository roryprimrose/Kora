namespace Kora.Core.Interaction;

public sealed record LocalEventView(LocalEvent Event, LocalEventReason Reason, DateTimeOffset? DeferredUntil)
{
    public void Validate()
    {
        Event.Validate();
        if (!Enum.IsDefined(Reason) || DeferredUntil is { } deferred
            && (deferred.Offset != TimeSpan.Zero || deferred <= Event.ObservedAt || deferred > Event.ExpiresAt))
        { throw new InvalidDataException("The local event view reason or deferral is invalid."); }
    }

    public string DisplaySummary => Reason == LocalEventReason.Eligible
        ? Event.Summary : "No new notification: " + Reason + ". Exact passive review remains separate.";
}
