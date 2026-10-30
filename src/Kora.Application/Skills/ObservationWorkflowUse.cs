using Kora.Core.Skills;
using Kora.Core.Storage;

namespace Kora.Application.Skills;

/// <summary>Host-owned one-shot state; the saved declaration never contains enablement or permission.</summary>
public sealed class ObservationWorkflowUse : IDisposable
{
    private readonly Lock gate = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly CancellationToken token;
    private ObservationWorkflowState state = ObservationWorkflowState.Reviewed;

    internal ObservationWorkflowUse(ObservationSkillWorkflow owner, OwnedSkillUseReview source,
        ObservationWorkflowDefinition definition, SessionWorkspaceEntry target, string request,
        OwnedSkillAuthoringAuthority authority, Func<bool> current, SessionProviderProfile profile)
    { Owner = owner; Source = source; Definition = definition; Target = target; Request = request; Authority = authority; Current = current; Profile = profile; token = cancellation.Token; }

    internal ObservationSkillWorkflow Owner { get; }
    internal OwnedSkillUseReview Source { get; }
    internal OwnedSkillAuthoringAuthority Authority { get; }
    internal Func<bool> Current { get; }
    internal SessionProviderProfile Profile { get; }
    internal CancellationToken Token => token;
    public ObservationWorkflowDefinition Definition { get; }
    public SessionWorkspaceEntry Target { get; }
    public string Request { get; }
    public string SourceText => Source.Text;
    public ObservationWorkflowState State { get { lock (gate) { return state; } } }

    internal void Transition(ObservationWorkflowState expected, ObservationWorkflowState next)
    {
        lock (gate)
        {
            if (state != expected) { throw new InvalidOperationException("Review, separately enable and explicitly invoke this workflow once."); }
            state = next;
        }
    }

    internal void Finish(ObservationWorkflowState terminal)
    {
        lock (gate) { state = terminal; cancellation.Dispose(); }
        Source.Dispose();
    }

    public void Cancel()
    {
        lock (gate)
        {
            if (state is ObservationWorkflowState.Succeeded or ObservationWorkflowState.Failed or ObservationWorkflowState.Cancelled) { return; }
            if (state is ObservationWorkflowState.Running) { state = ObservationWorkflowState.Cancelling; }
            cancellation.Cancel();
        }
    }

    public void Dispose()
    {
        Cancel();
        Source.Dispose();
        if (State is ObservationWorkflowState.Reviewed or ObservationWorkflowState.Enabled)
        { Finish(ObservationWorkflowState.Cancelled); }
    }
}
