namespace Kora.Core.Agents;

public sealed record AgentProfileWriteResult(AgentProfileWriteState State, OwnedAgentProfileSnapshot? Snapshot);
