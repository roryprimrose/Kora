namespace Kora.Core.Hosting;

public sealed record HostRequest
{
    public HostRequest(
        HostId<RequestIdentity> requestId,
        HostId<SessionIdentity> sessionId,
        HostId<TaskIdentity> taskId,
        RequestOrigin origin,
        HostId<InvocationIdentity>? invocationId = null)
    {
        requestId.Validate();
        sessionId.Validate();
        taskId.Validate();
        invocationId?.Validate();
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }
        RequestId = requestId;
        SessionId = sessionId;
        TaskId = taskId;
        Origin = origin;
        InvocationId = invocationId;
    }

    public HostId<RequestIdentity> RequestId { get; }
    public HostId<SessionIdentity> SessionId { get; }
    public HostId<TaskIdentity> TaskId { get; }
    public RequestOrigin Origin { get; }
    public HostId<InvocationIdentity>? InvocationId { get; }

    public bool IsWithinIntent(HostRequest intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        // A root intent can own invocation-scoped children; a bound invocation never widens.
        return RequestId == intent.RequestId && SessionId == intent.SessionId
            && TaskId == intent.TaskId && Origin == intent.Origin
            && (intent.InvocationId is not { } invocation
                || (InvocationId is { } current && current == invocation));
    }

    // Only the host calls this factory; provider correlation is never an argument.
    public static HostRequest Create(RequestOrigin origin) =>
        new(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), origin);
}
