namespace Kora.Core.Hosting;

public enum SessionQueueEligibility
{
    Ready, Completed, Current, UnknownQuarantine, InterruptedNoReplay, Expired,
    AdmissionChanged, SessionDone, UnclassifiedWorkOrWait, SessionCurrent,
    DependencyNotSucceeded, EarlierPendingEntry, GlobalCapacity
}
