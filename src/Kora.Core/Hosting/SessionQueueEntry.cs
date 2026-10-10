using System.Text.Json.Serialization;

namespace Kora.Core.Hosting;

/// <summary>Content-free deterministic work identity, not an executable token or grant.</summary>
public sealed record SessionQueueEntry(
    HostRequest Request, HostRevision Generation, HostRevision Revision, long Position,
    SessionQueueState State, Guid RunId, long AdmissionRevision, DateTimeOffset EnqueuedAt,
    DateTimeOffset ExpiresAt, HostId<TaskIdentity>? Dependency = null, long DispatchOrder = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? RecordVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PendingLifetimeMinutes = null)
{
    public const int CapturedLifetimeRecordVersion = 2;
    public bool IsPending => State == SessionQueueState.Pending;
    public bool IsCurrent => State == SessionQueueState.Running;

    public void Validate()
    {
        if (Request is null) { throw new InvalidDataException("The queue work identity is missing."); }
        Request.SessionId.Validate();
        Request.TaskId.Validate();
        Request.RequestId.Validate();
        Dependency?.Validate();
        // Absent fields are the original fixed-30 format. Never add fields when rewriting legacy rows:
        // their canonical payload and original committed authority digest must remain identical.
        var minutes = SessionQueueLimits.DefaultPendingLifetimeMinutes;
        if (RecordVersion is not null || PendingLifetimeMinutes is not null)
        {
            if (RecordVersion != CapturedLifetimeRecordVersion || PendingLifetimeMinutes is not { } captured)
            {
                throw new InvalidDataException("The queue lifetime record version or captured value is unknown.");
            }
            try { SessionQueueLimits.ValidatePendingLifetimeMinutes(captured); }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new InvalidDataException("The captured queue pending lifetime is invalid.", exception);
            }
            minutes = captured;
        }
        if (Generation.Value <= 0 || Revision.Value <= 0 || Position <= 0 || RunId == Guid.Empty
            || AdmissionRevision < 0 || DispatchOrder < 0 || !Enum.IsDefined(State)
            || ExpiresAt - EnqueuedAt != TimeSpan.FromMinutes(minutes)
            || Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || Dependency == Request.TaskId)
        {
            throw new InvalidDataException("The deterministic queue identity, revision or lifetime is invalid.");
        }
    }
}
