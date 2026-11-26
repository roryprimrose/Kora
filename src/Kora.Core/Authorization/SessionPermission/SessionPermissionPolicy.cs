using Kora.Core.Hosting;

namespace Kora.Core.Authorization.SessionPermission;

public sealed record SessionPermissionPolicy(
    HostId<SessionIdentity> SessionId, HostRevision Generation, HostRevision Revision, SessionPermissionLevel Level)
{
    public static SessionPermissionPolicy Default(WorkSessionAuthorization session) =>
        new(session.SessionId, session.Generation, new(1), SessionPermissionLevel.ReviewAll);

    public void Validate(WorkSessionAuthorization session)
    {
        if (SessionId != session.SessionId || Generation != session.Generation || Revision.Value <= 0
            || !Enum.IsDefined(Level) || !session.IsActive && Level != SessionPermissionLevel.ReviewAll)
        { throw new InvalidDataException("The exact session permission policy is corrupt or belongs to another lifecycle."); }
    }

    public string DisplayName => Level switch
    {
        SessionPermissionLevel.ReviewAll => "Review all",
        SessionPermissionLevel.ModelReview => "Model review",
        SessionPermissionLevel.ApproveAll => "Approve all",
        _ => throw new InvalidDataException("The saved session permission level is unknown."),
    };

    public const string ApproveAllWarning =
        "Kora-owned action, script, process, protected-control and data-disclosure reviews are automatically approved in this session. "
        + "Grants are not evaluated, consumed or created. Every automatic approval requires a trusted audit record. "
        + "Authentication, microphone, memory, hard denials and physical resource joins still apply.";
}
