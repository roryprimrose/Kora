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
        "Actions without grants will run without safety review or ordinary approval in this session. "
        + "No grants will be saved. Independent consent and mandatory protections still apply.";
}
