using System.Collections.Immutable;

using Kora.Core.Interaction;

namespace Kora.Core.Authorization;

/// <summary>All reviewed grants were revoked, or none; exceptions convey unavailable commit certainty.</summary>
public sealed record ExactGrantBatchDecision(
    HostInteractionOutcome Outcome, string Reason, ImmutableArray<OperationGrant> RevokedGrants);