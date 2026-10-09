using System.Xml.Linq;
using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Core.Authorization;
using Kora.Core.Communication;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.Communication;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class CallPolicyCompositionTests
{
    [Theory]
    [InlineData("session")]
    [InlineData("perpetual")]
    public async Task Real_durable_reuse_respects_manual_layer_without_revoking_or_consuming(string scope)
    {
        using var f = new InteractionStorageFixture();
        using var policy = new CallCommunicationPolicy(new UnavailableCallStateService());
        await f.InitializeAsync();
        var grant = await f.GrantAsync(scope);
        policy.SetManual(true, RequestOrigin.LocalUi, policy.Current.Revision, static () => true);
        f.Policy = policy.Current.Authorization(true, true);
        await f.PublishAsync();
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, f.Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Denied);
        (await f.Store.ReadGrantsAsync(f.Token)).Single().Should().Be(grant);
        var callRequest = f.Request;
        f.Request = InteractionStorageFixture.NewRequest(callRequest.SessionId);
        await f.AdmitAsync(newSession: false);
        var once = await f.GrantAsync("once");
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, once.Id, once.Revision, f.Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Consumed);
        policy.SetManual(false, RequestOrigin.LocalUi, policy.Current.Revision, static () => true);
        f.Policy = policy.Current.Authorization(true, true);
        await f.PublishAsync();
        policy.Current.AutomaticState.Should().Be(CallState.Unavailable);
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, f.Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Consumed);
        var persisted = (await f.Store.ReadGrantsAsync(f.Token)).Single(g => g.Id == grant.Id);
        persisted.Status.Should().Be(OperationGrantStatus.Active);
        persisted.UseCount.Should().Be(1);
    }

    [Fact]
    public async Task Real_durable_settings_proposal_retains_original_voice_origin_despite_UI_approval()
    {
        using var f = new InteractionStorageFixture();
        using var policy = new CallCommunicationPolicy(new UnavailableCallStateService());
        f.Request = InteractionStorageFixture.NewRequest(origin: RequestOrigin.ActivatedVoice);
        await f.InitializeAsync();
        f.Proposal = new(f.Request, f.Proposal.ProposalId, f.Proposal.Revision, f.Proposal.Binding,
            HostOperationEffect.VoiceOrCallSettings, f.Proposal.ExpiresAt);
        await f.PublishAsync();
        var question = await f.PresentAsync();
        policy.SetManual(true, RequestOrigin.LocalUi, policy.Current.Revision, static () => true);
        f.Policy = policy.Current.Authorization(true, true);
        await f.PublishAsync();
        (await f.RunAsync(() => f.Authorization.ApproveAsync(question.Key, new(["once"]), RequestOrigin.LocalUi, f.Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Denied);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
        (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token)).Single().Key.Request.Origin
            .Should().Be(RequestOrigin.ActivatedVoice);
        f.Request = InteractionStorageFixture.NewRequest(f.Request.SessionId, RequestOrigin.LocalUi);
        await f.AdmitAsync(newSession: false);
        f.Proposal = new(f.Request, f.Proposal.ProposalId, f.Proposal.Revision, f.Proposal.Binding,
            HostOperationEffect.VoiceOrCallSettings, f.Proposal.ExpiresAt);
        await f.PublishAsync();
        (await f.GrantAsync("once")).ApprovedProposal.Request.Origin.Should().Be(RequestOrigin.LocalUi);
    }

    [Fact]
    public async Task Real_durable_pre_call_single_use_and_pending_reviews_are_invalidated_by_host_policy_revision()
    {
        using var f = new InteractionStorageFixture();
        using var policy = new CallCommunicationPolicy(new UnavailableCallStateService());
        await f.InitializeAsync();
        var once = await f.GrantAsync("once");
        policy.SetManual(true, RequestOrigin.LocalUi, policy.Current.Revision, static () => true);
        f.Policy = policy.Current.Authorization(true, true);
        var old = f.Proposal.Binding;
        f.Proposal = new(f.Request, f.Proposal.ProposalId, new(2),
            new(old.ActionId, old.SourcePartition, old.SkillId, old.DefinitionDigest, old.DeclaredResourceDigest,
                old.TrackedContentDigest, old.ImplementationDigest, old.InvocationDigest, old.ResourceDigest, old.IdentityDigest,
                old.DestinationDigest, old.TransformationDigest, new(policy.Current.Revision + 1)),
            f.Proposal.Effect, f.Proposal.ExpiresAt);
        await f.PublishAsync();
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, once.Id, once.Revision, f.Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Denied);
        (await f.Store.ReadGrantsAsync(f.Token)).Single().UseCount.Should().Be(0);
        var callQuestion = await f.PresentAsync();
        policy.SetManual(false, RequestOrigin.LocalUi, policy.Current.Revision, static () => true);
        f.Policy = policy.Current.Authorization(true, true);
        f.Proposal = new(f.Request, f.Proposal.ProposalId, new(3), f.Proposal.Binding, f.Proposal.Effect, f.Proposal.ExpiresAt);
        await f.PublishAsync();
        (await f.RunAsync(() => f.Authorization.ApproveAsync(callQuestion.Key, new(["once"]), RequestOrigin.LocalUi, f.Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Denied);
    }

    [Fact]
    public void Native_calls_tab_has_real_accessible_manual_controls_and_truthful_status_bindings()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Kora.slnx"))) { root = root.Parent; }
        var document = XDocument.Load(Path.Combine(root!.FullName, "src", "Kora", "SettingsWindow.axaml"));
        var tab = document.Descendants().Single(e => string.Equals(e.Name.LocalName, "TabItem", StringComparison.Ordinal)
            && string.Equals(e.Attribute("Header")?.Value, "Calls", StringComparison.Ordinal));
        foreach (var command in new[] { "EnableManualCallCommand", "ClearManualCallCommand", "ResetManualCallCommand", "GetManualCallStatusCommand" })
        {
            var button = tab.Descendants().Single(e => string.Equals(e.Attribute("Command")?.Value,
                "{Binding " + command + "}", StringComparison.Ordinal));
            button.Attribute("AutomationProperties.Name")!.Value.Should().NotBeNullOrWhiteSpace();
            button.Attribute("TabIndex").Should().NotBeNull();
        }
        foreach (var status in new[] { "CallStateStatus", "CallManualStatus", "ManualCallConfigurationStatus", "CallProtectionLimitations" })
        {
            tab.Descendants().Should().Contain(e => e.Attribute("Text") != null && e.Attribute("Text")!.Value == "{Binding " + status + "}");
        }
    }
}
