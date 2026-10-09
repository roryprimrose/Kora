namespace Kora.Core.Hosting;

/// <summary>Content-free deterministic work identity, not an executable token or grant.</summary>
public sealed record SessionQueueEntry(
    HostRequest Request, HostRevision Generation, HostRevision Revision, long Position,
    SessionQueueState State, Guid RunId, long AdmissionRevision, DateTimeOffset EnqueuedAt,
    DateTimeOffset ExpiresAt, HostId<TaskIdentity>? Dependency = null, long DispatchOrder = 0)
{
    public bool IsPending => State == SessionQueueState.Pending;
    public bool IsCurrent => State == SessionQueueState.Running;

    public void Validate()
    {
        if (Request is null) { throw new InvalidDataException("The queue work identity is missing."); }
        Request.SessionId.Validate();
        Request.TaskId.Validate();
        Request.RequestId.Validate();
        Dependency?.Validate();
        if (Generation.Value <= 0 || Revision.Value <= 0 || Position <= 0 || RunId == Guid.Empty
            || AdmissionRevision < 0 || DispatchOrder < 0 || !Enum.IsDefined(State)
            || ExpiresAt != EnqueuedAt.Add(SessionQueuePolicy.PendingLifetime)
            || Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || Dependency == Request.TaskId)
        {
            throw new InvalidDataException("The deterministic queue identity, revision or lifetime is invalid.");
        }
    }
}
