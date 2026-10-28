using Kora.Application.Agents;
using Kora.Core.Agents;

namespace Kora;

internal sealed partial class AgentRunController
{
    private ProposedBoundedAgentUse? modelProposal;

    internal async Task ReviewProposalAsync(ProposedBoundedAgentUse proposal)
    {
        await proposal.ConsumeForReviewAsync(CancellationToken.None);
        await Route(new("Open", string.Empty), proposal.Origin, proposal.IsCurrent);
        var opened = window ?? throw new InvalidOperationException("The private agent review could not be opened.");
        active = PrepareProposalReportedAsync(proposal, opened);
        await JoinActive();
        if (!ReferenceEquals(window, opened))
        {
            await CloseAndWait();
            throw new InvalidOperationException("The exact registered agent proposal review was unavailable or revoked.");
        }
    }

    private async Task PrepareProposalReportedAsync(ProposedBoundedAgentUse proposal, AgentRunWindow opened)
    {
        try { await PrepareProposalAsync(proposal, opened); }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException
            or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Failed(logger, exception.GetType().Name);
            main.ReportHostInteractionFailure("The proposed exact agent source is unavailable or revoked. No agent ran; make a fresh request.");
            Close();
        }
    }

    private async Task PrepareProposalAsync(ProposedBoundedAgentUse proposal, AgentRunWindow opened)
    {
        var resolver = namedAgents ?? throw new InvalidOperationException("Registered agent source resolution is unavailable.");
        var token = cancellation!.Token;
        var selected = await resolver.ResolveAsync(proposal.Definition.Name,
            () => HostEligible() && proposal.IsCurrent() && ReferenceEquals(window, opened), token);
        if (selected.Definition != proposal.Definition)
        { throw new InvalidOperationException("The proposed agent is not the exact registered source."); }
        await proposal.ValidateAsync(token);
        if (!HostEligible() || !ReferenceEquals(window, opened))
        { throw new InvalidOperationException("The original proposal review was closed or revoked."); }
        modelProposal = proposal;
        namedSelection = selected;
        namedEligibility = () => proposal.IsCurrent() && selected.IsCurrent();
        namedCommand = new(proposal.Definition.Name, proposal.Input);
        opened.Source.Text = selected.Path;
        opened.Input.Text = proposal.Input;
        namedContentRevision = contentRevision;
        inputOrigin = proposal.Origin;
        opened.Source.IsReadOnly = opened.Input.IsReadOnly = true;
        var target = new AgentTarget(proposal.Target);
        opened.Targets.ItemsSource = new[] { target };
        opened.Targets.SelectedItem = target;
        opened.Targets.IsEnabled = false;
        opened.Status.Text = "Untrusted model recommendation only; no agent has run. The original conversation, source and request are fixed. "
            + "Review the complete exact invocation, then separately confirm once.";
    }

    private void ValidateProposedProfile(BoundedAgentProfile profile)
    {
        if (modelProposal is not null && (!string.Equals(profile.Document.Metadata["kora-agent-format"], BoundedAgentProfile.FormatVersion, StringComparison.Ordinal)
            || !profile.Tools.IsEmpty || !profile.References.IsEmpty || profile.SharedSkill is not null))
        { throw new InvalidOperationException("Model-proposed invocation admits only instruction-only local v1 agents with tools and references set to none."); }
    }
}
