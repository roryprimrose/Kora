namespace Kora.Core.Authorization;

/// <summary>Passive retained metadata, not applicability, execution or original-session authority.</summary>
public sealed record ExactGrantInspection(
    string StoreIdentity,
    OperationGrant Grant,
    WorkSessionAuthorization? OriginSession,
    bool OriginSessionRemoved);
