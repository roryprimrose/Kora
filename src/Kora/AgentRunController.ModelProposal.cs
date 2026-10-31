using Kora.Application.Agents;
using Kora.Core.Agents;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class AgentRunController
{
    private ProposedBoundedAgentUse? modelProposal;

    internal async Task ReviewProposalAsync(ProposedBoundedAgentUse proposal)
    {
        await proposal.ConsumeForReviewAsync(CancellationToken.None);
        await Route(new("Open", string.Empty), RequestOrigin.LocalUi, proposal.IsCurrent);
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
        await proposal.ValidateAsync(token);
        if (!HostEligible() || !ReferenceEquals(window, opened))
        { throw new InvalidOperationException("The original proposal review was closed or revoked."); }
        var candidates = resolver.ReadCandidates(proposal.Definition.Name,
            () => HostEligible() && proposal.IsCurrent() && ReferenceEquals(window, opened), token);
        var choices = new List<ProposalSource>();
        foreach (var candidate in candidates.Where(item => item.IsUseEnabled && !item.IsUseDisabled && item.UseControlId is not null))
        {
            var selection = await resolver.ResolveAsync(candidate,
                () => HostEligible() && proposal.IsCurrent() && ReferenceEquals(window, opened), token);
            choices.Add(new(selection));
        }
        if (choices.Count == 0)
        { throw new InvalidOperationException("No current registered agent candidate matches the inert recommendation."); }
        modelProposal = proposal;
        namedEligibility = proposal.IsCurrent;
        opened.Source.IsReadOnly = true;
        opened.RecommendedSources.ItemsSource = choices.ToArray();
        opened.RecommendedSources.SelectedIndex = -1;
        opened.RecommendedSources.IsVisible = true;
        opened.RecommendedSources.SelectionChanged += (_, _) =>
        {
            if (!ReferenceEquals(window, opened)) { return; }
            namedSelection = null;
            namedEligibility = proposal.IsCurrent;
            opened.Source.Text = string.Empty;
            Retire();
        };
        opened.Recommendation.Text = "UNTRUSTED RECOMMENDATION ONLY\nMetadata question (not an invocation task):\n"
            + proposal.DiscoveryQuestion + "\nDiscovery channel: " + proposal.DiscoveryOrigin
            + "\nChoose a current source below, then enter a NEW original task. No source body has been read.";
        opened.Recommendation.IsVisible = true;
        var target = new AgentTarget(proposal.Target);
        opened.Targets.ItemsSource = new[] { target };
        opened.Targets.SelectedItem = target;
        opened.Targets.IsEnabled = false;
        opened.Status.Text = "The original conversation is fixed, not the discovery question. Only current enabled file-bound sources are offered. "
            + "No source is selected automatically, including same-named sources. "
            + "Choose the exact current source and supply a new task; source reading and invocation confirmation remain separate.";
    }

    private async Task SelectProposedSourceAsync(AgentRunWindow opened, Func<bool> current, CancellationToken token)
    {
        var proposal = modelProposal ?? throw new InvalidOperationException("No current recommendation is available.");
        var choice = (opened.RecommendedSources.SelectedItem as ProposalSource)?.Selection.Definition
            ?? throw new InvalidOperationException("Choose one exact current source; a recommendation never selects it for you.");
        if (!string.Equals(choice.Name, proposal.Definition.Name, StringComparison.Ordinal))
        { throw new InvalidOperationException("The chosen source is not a candidate for this recommendation."); }
        await proposal.ValidateAsync(token);
        if (!current()) { throw new InvalidOperationException("The source choice or new task changed during review."); }
        var selected = await (namedAgents ?? throw new InvalidOperationException("Registered agent source resolution is unavailable."))
            .ResolveAsync(choice, () => HostEligible() && proposal.IsCurrent() && ReferenceEquals(window, opened), token);
        if (!current()) { throw new InvalidOperationException("The source choice or new task changed during source resolution."); }
        namedSelection = selected;
        namedEligibility = () => proposal.IsCurrent() && selected.IsCurrent();
        opened.Source.Text = selected.Path;
    }

    private void ValidateProposedProfile(BoundedAgentProfile profile)
    {
        if (modelProposal is not null && (!string.Equals(profile.Document.Metadata["kora-agent-format"], BoundedAgentProfile.FormatVersion, StringComparison.Ordinal)
            || !profile.Tools.IsEmpty || !profile.References.IsEmpty || profile.SharedSkill is not null))
        { throw new InvalidOperationException("Model-proposed invocation admits only instruction-only local v1 agents with tools and references set to none."); }
    }

    private sealed record ProposalSource(NamedAgentSelection Selection)
    {
        public override string ToString() => $"Agent / {Selection.Definition.Name} / PRIVATE source: {Selection.Path} / "
            + "Enabled; executable availability not established";
    }
}
