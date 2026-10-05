namespace Kora.Core.Coordination;

public sealed record InstanceProcessIdentity(
    string UserSid,
    int SessionId,
    int ProcessId,
    long CreationTime,
    InstanceBuildIdentity Build);
