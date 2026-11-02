namespace Kora.Core.Agents;

public interface IOwnedAgentProfileStore
{
    Task<IReadOnlyList<string>> List(CancellationToken token);
    Task<OwnedAgentProfileSnapshot?> Read(string name, CancellationToken token);
    Task<OwnedAgentProfileDestination> PrepareNew(string name, CancellationToken token);
    Task<AgentProfileWriteResult> Create(OwnedAgentProfileDestination destination, OwnedAgentProfileDocument document,
        Func<bool> eligible, Func<Func<AgentProfileWriteResult>, Task<AgentProfileWriteResult>> commit, CancellationToken token);
    Task<AgentProfileWriteResult> Update(OwnedAgentProfileSnapshot expected, OwnedAgentProfileDocument document,
        Func<bool> eligible, Func<Func<AgentProfileWriteResult>, Task<AgentProfileWriteResult>> commit, CancellationToken token);
}
