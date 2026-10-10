using System.Text.Json.Serialization;

namespace Kora.Core.Hosting;

/// <summary>Content-free deterministic work identity, not an executable token or grant.</summary>
public sealed record SessionQueueEntry(
    HostRequest Request, HostRevision Generation, HostRevision Revision, long Position,
    SessionQueueState State, Guid RunId, long AdmissionRevision, DateTimeOffset EnqueuedAt,
    DateTimeOffset ExpiresAt, HostId<TaskIdentity>? Dependency = null, long DispatchOrder = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? RecordVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PendingLifetimeMinutes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ActiveBudgetMinutes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? AdmittedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ActiveDeadlineAt = null)
{
    public const int CapturedLifetimeRecordVersion = 2;
    public const int CapturedActiveRecordVersion = 3;
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
            if (RecordVersion is not (CapturedLifetimeRecordVersion or CapturedActiveRecordVersion)
                || PendingLifetimeMinutes is not { } captured)
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
        if (RecordVersion == CapturedActiveRecordVersion)
        {
            if (ActiveBudgetMinutes is not { } budget || AdmittedAt is not { } admitted
                || ActiveDeadlineAt is not { } deadline || admitted.Offset != TimeSpan.Zero
                || admitted < EnqueuedAt || DispatchOrder <= Position || Revision.Value < 2
                || State is not (SessionQueueState.Running or SessionQueueState.Succeeded or SessionQueueState.Failed
                    or SessionQueueState.Unknown))
            {
                throw new InvalidDataException("The captured active admission is incomplete or unbound.");
            }
            try
            {
                if (deadline != SessionQueueLimits.ActiveDeadlineAt(admitted, budget) || deadline.Offset != TimeSpan.Zero)
                {
                    throw new InvalidDataException("The captured active deadline differs from its original admission budget.");
                }
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new InvalidDataException("The captured active budget or deadline is invalid.", exception);
            }
        }
        else if (ActiveBudgetMinutes is not null || AdmittedAt is not null || ActiveDeadlineAt is not null)
        {
            throw new InvalidDataException("Captured active authority requires the explicit active record version.");
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

    public SessionQueueEntry Admit(HostRevision revision, long dispatchOrder, DateTimeOffset admittedAt, int activeBudgetMinutes)
    {
        Validate();
        if (!IsPending || DispatchOrder != 0) { throw new InvalidOperationException("Only a pending fixed read can capture active admission."); }
        var admitted = this with
        {
            Revision = revision, State = SessionQueueState.Running, DispatchOrder = dispatchOrder,
            RecordVersion = CapturedActiveRecordVersion,
            PendingLifetimeMinutes = PendingLifetimeMinutes ?? SessionQueueLimits.DefaultPendingLifetimeMinutes,
            ActiveBudgetMinutes = activeBudgetMinutes, AdmittedAt = admittedAt,
            ActiveDeadlineAt = SessionQueueLimits.ActiveDeadlineAt(admittedAt, activeBudgetMinutes),
        };
        admitted.Validate();
        return admitted;
    }
}
