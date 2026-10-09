namespace Kora.Core.Memory;

public enum MemoryReason
{
    None, HostContextRequired, UserIntentRequired, AuthorityClosed, ScopeMismatch,
    LineageUnknown, ContentForbidden, ValueInvalid, ValueLimitExceeded, PayloadLimitExceeded,
    NotReviewed, NotEnabled, DisclosureNotAdmitted, RevisionMismatch, StateMismatch,
    Missing, Capacity, CallerCancelled,
}
