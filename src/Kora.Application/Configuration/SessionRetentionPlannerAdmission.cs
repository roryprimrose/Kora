namespace Kora.Application.Configuration;

public sealed record SessionRetentionPlannerAdmission(long ControlRevision, long PrivateRevision, long OwnerRevision, long CallRevision);
