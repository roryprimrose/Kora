namespace Kora.Core.Hosting;

public enum HostTaskState
{
    IntentRecorded,
    DispatchRecorded,
    Succeeded,
    Failed,
    Denied,
    Cancelled,
    Interrupted,
    Unknown,
    Unavailable,
}
