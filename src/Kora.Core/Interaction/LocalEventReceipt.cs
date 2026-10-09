namespace Kora.Core.Interaction;

public sealed record LocalEventReceipt(
    LocalEvent Event, LocalEventDisposition Disposition, DateTimeOffset? DeferredUntil, DateTimeOffset ChangedAt);
