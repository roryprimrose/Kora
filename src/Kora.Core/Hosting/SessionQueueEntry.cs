using System.Text.Json.Serialization;
using Kora.Core.Tools;

namespace Kora.Core.Hosting;

/// <summary>Deterministic work identity and optional bounded fixed metadata result, not an executable token or grant.</summary>
public sealed record SessionQueueEntry(
    HostRequest Request, HostRevision Generation, HostRevision Revision, long Position,
    SessionQueueState State, Guid RunId, long AdmissionRevision, DateTimeOffset EnqueuedAt,
    DateTimeOffset ExpiresAt, HostId<TaskIdentity>? Dependency = null, long DispatchOrder = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? RecordVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PendingLifetimeMinutes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ActiveBudgetMinutes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? AdmittedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ActiveDeadlineAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SessionQueueReadProfile? Profile = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CapabilityReply? Result = null)
{
    public const int CapturedLifetimeRecordVersion = 2;
    public const int CapturedActiveRecordVersion = 3;
    public const int FixedReadRecordVersion = 4;
    [JsonIgnore] public SessionQueueReadProfile ReadProfile => Profile ?? SessionQueueReadProfile.Version;
    public bool IsPending => State == SessionQueueState.Pending;
    public bool IsCurrent => State == SessionQueueState.Running;

    public void Validate()
    {
        if (Request is null) { throw new InvalidDataException("The queue work identity is missing."); }
        Request.SessionId.Validate();
        Request.TaskId.Validate();
        Request.RequestId.Validate();
        Dependency?.Validate();
        if (RecordVersion == FixedReadRecordVersion)
        {
            if (Profile is not { } profile) { throw new InvalidDataException("The fixed read profile is missing."); }
            _ = SessionQueueReadProfiles.CapabilityId(profile);
            if (DispatchOrder == 0 && State is not (SessionQueueState.Pending or SessionQueueState.Cancelled
                or SessionQueueState.Removed or SessionQueueState.Expired or SessionQueueState.Interrupted))
            {
                throw new InvalidDataException("The fixed read has no captured active admission.");
            }
            if (Result is not null && (State != SessionQueueState.Succeeded || !SessionQueueReadProfiles.Accepts(profile, Result)))
            {
                throw new InvalidDataException("The fixed read result is not a bounded matching success.");
            }
        }
        else if (Profile is not null || Result is not null)
        {
            throw new InvalidDataException("A fixed read profile/result requires its explicit record version.");
        }
        // Absent fields are the original fixed-30 format. Never add fields when rewriting legacy rows:
        // their canonical payload and original committed authority digest must remain identical.
        var minutes = SessionQueueLimits.DefaultPendingLifetimeMinutes;
        if (RecordVersion is not null || PendingLifetimeMinutes is not null)
        {
            if (RecordVersion is not (CapturedLifetimeRecordVersion or CapturedActiveRecordVersion or FixedReadRecordVersion)
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
        if (RecordVersion == CapturedActiveRecordVersion || RecordVersion == FixedReadRecordVersion && DispatchOrder > 0)
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
            RecordVersion = Profile is null ? CapturedActiveRecordVersion : FixedReadRecordVersion,
            PendingLifetimeMinutes = PendingLifetimeMinutes ?? SessionQueueLimits.DefaultPendingLifetimeMinutes,
            ActiveBudgetMinutes = activeBudgetMinutes, AdmittedAt = admittedAt,
            ActiveDeadlineAt = SessionQueueLimits.ActiveDeadlineAt(admittedAt, activeBudgetMinutes),
        };
        admitted.Validate();
        return admitted;
    }

    public void ValidateStoredResult()
    {
        Validate();
        if (RecordVersion == FixedReadRecordVersion && State == SessionQueueState.Succeeded && Result is null)
        {
            throw new InvalidDataException("The committed fixed read success has no typed result.");
        }
    }
}
