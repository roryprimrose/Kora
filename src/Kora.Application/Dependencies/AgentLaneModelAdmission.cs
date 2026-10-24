using Kora.Application.Agents;
using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

/// <summary>Per-workflow adapter to the same authoritative host; never a provider or policy replacement.</summary>
public sealed class AgentLaneModelAdmission : IModelTurnAdmission, IModelTurnExecution
{
    private readonly ModelTurnHost host;
    private readonly ILocalAgentConcurrencyQualification qualification;
    private BoundedAgentReview? agentReview;
    private LocalAgentExecutionLane? lane;

    internal AgentLaneModelAdmission(ModelTurnHost host, ILocalAgentConcurrencyQualification qualification)
    {
        this.host = host;
        this.qualification = qualification;
    }

    internal void Bind(BoundedAgentReview value)
    {
        if (agentReview is not null) { throw new InvalidOperationException("A lane review cannot be replaced."); }
        if (!value.Profile.Tools.IsEmpty)
        { throw new InvalidOperationException("Independent runs require an existing local tool-free agent profile."); }
        agentReview = value;
    }

    // SessionConversationService asks whether submission can proceed, not whether another lane has completed.
    public bool IsQuiescent => lane is { } issued ? issued.Completion.IsCompleted
        : agentReview is { } captured && host.CanSubmitLocalAgent(captured, qualification);

    public async Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
        ModelHandoffReview? review, CancellationToken token)
    {
        if (choice != ModelTurnChoice.Local || review is not null || agentReview is not { } captured || lane is not null)
        { throw new InvalidOperationException("Only this workflow's single original local agent invocation is admitted."); }
        var admission = await host.AdmitAsync(choice, context, review, token).ConfigureAwait(false);
        if (admission.Turn is { } turn)
        { lane = host.IssueLocalAgentLane(captured, turn, qualification); }
        return admission;
    }

    public Task<ModelTurnResult> RunAsync(ModelTurn turn, Func<bool> admitted, CancellationToken token,
        Func<CancellationToken, Task>? validateSource = null)
    {
        if (lane is null || !ReferenceEquals(lane.Turn, turn))
        { throw new InvalidOperationException("The exact host-issued agent lane is required."); }
        return ((IModelTurnExecution)host).RunAsync(turn, admitted, token, validateSource);
    }

    public async Task WaitForQuiescenceAsync(CancellationToken cancellationToken)
    {
        if (lane is { } captured)
        {
#pragma warning disable VSTHRD003 // Observe only this host-issued lane, including its actual late adapter completion.
            await captured.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
    }
}
