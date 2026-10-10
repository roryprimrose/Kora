namespace Kora.Core.Memory;

public enum MemoryOutcome
{
    Succeeded, Denied, Cancelled, RevisionConflict, InvalidTransition, NotFound, CapacityExceeded,
}
