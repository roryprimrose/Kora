using Kora.Core.Hosting;

namespace Kora.Core.Skills;

// The consumer validates every field against its host-owned invocation; this record is not a grant.
public sealed record BundledScriptReceipt(HostRequest Invocation, FixedSessionAction Action,
    string DeclaredResourceDigest, bool ScriptsCompleted, bool ResourcesJoined);