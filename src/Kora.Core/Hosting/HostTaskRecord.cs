namespace Kora.Core.Hosting;

public sealed record HostTaskRecord
{
    public HostTaskRecord(HostRequest request, HostRevision revision, HostTaskState state)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (revision.Value <= 0 || !Enum.IsDefined(state))
        {
            throw new InvalidDataException("The task revision or state is invalid.");
        }
        Request = request;
        Revision = revision;
        State = state;
    }

    public HostRequest Request { get; }
    public HostRevision Revision { get; }
    public HostTaskState State { get; }
    public bool IsTerminal => State is not (HostTaskState.IntentRecorded or HostTaskState.DispatchRecorded);

    public HostTaskRecord Recover() => State switch
    {
        HostTaskState.IntentRecorded => Next(HostTaskState.Interrupted),
        HostTaskState.DispatchRecorded => Next(HostTaskState.Unknown),
        _ => this,
    };

    public HostTaskRecord Next(HostTaskState next)
    {
        if (!Enum.IsDefined(next) || IsTerminal
            || next == HostTaskState.IntentRecorded
            || (next == HostTaskState.DispatchRecorded && State != HostTaskState.IntentRecorded)
            || (State == HostTaskState.DispatchRecorded && next is HostTaskState.Cancelled or HostTaskState.Interrupted))
        {
            throw new InvalidOperationException("The task transition cannot establish a truthful outcome.");
        }
        return new HostTaskRecord(Request, new HostRevision(checked(Revision.Value + 1)), next);
    }
}
