namespace Kora.Core.Commands;

public sealed record GrantChange(
    GrantChangeOperation Operation,
    BuiltInAction Action,
    ModelApprovalScope Scope,
    ModelApprovalScope? TargetScope = null);
