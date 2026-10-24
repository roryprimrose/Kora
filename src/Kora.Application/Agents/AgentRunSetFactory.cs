using Kora.Application.Dependencies;

namespace Kora.Application.Agents;

/// <summary>Creates separate canonical workflow owners while retaining one model host.</summary>
internal sealed class AgentRunSetFactory(ModelTurnHost host, ILocalAgentConcurrencyQualification qualification,
    Func<AgentLaneModelAdmission, BoundedAgentWorkflow> createWorkflow)
{
    public AgentRunSet Create() => new(this);

    internal (BoundedAgentWorkflow Workflow, AgentLaneModelAdmission Admission) CreateWorkflow()
    {
        var admission = new AgentLaneModelAdmission(host, qualification);
        return (createWorkflow(admission), admission);
    }
}
