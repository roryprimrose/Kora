using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Mcp;
using Kora.Core.Tools;
using Kora.Application.Dependencies;
using Kora.Application.Mcp;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Fact]
    public async Task Capability_disable_and_reenable_cannot_revive_an_inflight_model_turn()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var store = new Tools.KoraMcpServerTests.MemoryStore();
        var directories = new LocalArtifactDirectoryPreferences(store);
        var preferences = new McpCapabilityPreferences(new ArtifactUsePreferences(store),
            directories, new ArtifactDirectoryConfigurationService(directories, fixture));
        var host = fixture.Qualified(ModelProviderSelection.OllamaCandidate, capabilities: preferences);
        var admission = await host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), Token);
        fixture.Adapter.OnRun = () =>
        {
            preferences.SetToolEnabled(KoraMcpCatalog.ServerId, ReadOnlyCapabilityCatalog.Version, false);
            preferences.SetToolEnabled(KoraMcpCatalog.ServerId, ReadOnlyCapabilityCatalog.Version, true);
        };
        var result = await host.RunAsync(admission.Turn!, Token);
        result.Reason.Should().Be(ModelTurnReason.HostAdmissionClosed);
        result.Response.Should().BeNull();
        fixture.Adapter.Calls.Should().Be(1);
        host.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task Built_in_capability_changes_do_not_cancel_an_unrelated_tool_free_answer()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var store = new Tools.KoraMcpServerTests.MemoryStore();
        var directories = new LocalArtifactDirectoryPreferences(store);
        var preferences = new McpCapabilityPreferences(new ArtifactUsePreferences(store),
            directories, new ArtifactDirectoryConfigurationService(directories, fixture));
        var host = fixture.Qualified(ModelProviderSelection.OllamaCandidate, capabilities: preferences);
        var admission = await host.AdmitAsync(ModelProviderSelection.OllamaCandidate,
            fixture.Context(toolMode: ModelToolMode.None), Token);
        fixture.Adapter.OnRun = () => preferences.SetServerEnabled(KoraMcpCatalog.ServerId, false);
        var result = await host.RunAsync(admission.Turn!, Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        result.Response.Should().NotBeNull();
        fixture.Adapter.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shared_external_disable_and_reenable_cannot_consume_a_stale_pending_approval(bool parent)
    {
        using var fixture = new Fixture();
        using var source = new McpSource(audit: fixture);
        fixture.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        fixture.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var transport = new McpModelTransport();
        await using var loop = new BoundedModelTurnAdapter(transport, fixture.Time, fixture);
        var host = fixture.ConfiguredProfiles(adapter: loop, capabilities: source.Capabilities);
        var approvals = new McpApprovals(fixture);
        var broker = new McpBroker(source.Account);
        var service = McpService(fixture, host, source, broker, approvals, await source.Policy());
        async Task<McpApprovalPresentation> Present(HostOperationProposal proposal, HostRevision generation,
            McpReview review, Func<bool> admitted, CancellationToken token)
        {
            var result = await approvals.Present(proposal, generation, review, admitted, token);
            if (review.Stage == McpReviewStage.ToolSelection)
            {
                var change = HostRequest.Create(RequestOrigin.LocalUi);
                var previous = fixture.ExpectedAuditRequest;
                using (HostActivity.BeginRoot(change, HostActivityLayer.Application, HostOperation.Request))
                {
                    fixture.ExpectedAuditRequest = change;
                    try
                    {
                        if (parent)
                        {
                            source.Capabilities!.SetServerEnabled(source.ServerId, false);
                            source.Capabilities.SetServerEnabled(source.ServerId, true);
                        }
                        else
                        {
                            source.Capabilities!.SetToolEnabled(source.ServerId, "search", false);
                            source.Capabilities.SetToolEnabled(source.ServerId, "search", true);
                        }
                    }
                    finally { fixture.ExpectedAuditRequest = previous; }
                }
                admitted().Should().BeFalse();
            }
            return result;
        }
        var receipt = await service.SubmitMcpAsync(new(fixture.Session, null), "Find the reviewed reference.",
            source.ServerId, source.Account, Present, McpSelect, static () => true, Token);
        receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse.Should().BeNull();
        broker.Methods.Should().Equal("initialize", "tools/list");
        transport.Requests.Should().BeEmpty();
        approvals.Snapshots.Values.SelectMany(snapshot => snapshot.Grants)
            .Where(grant => grant.Status == OperationGrantStatus.Consumed).DistinctBy(grant => grant.Id)
            .Should().ContainSingle();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Composed_host_enforces_enabled_current_tools_without_dispatching_disabled_calls(bool disabled, bool late)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var store = new Tools.KoraMcpServerTests.MemoryStore();
        var directories = new LocalArtifactDirectoryPreferences(store);
        var preferences = new McpCapabilityPreferences(new ArtifactUsePreferences(store),
            directories, new ArtifactDirectoryConfigurationService(directories, fixture));
        if (disabled) { preferences.SetToolEnabled(KoraMcpCatalog.ServerId, ReadOnlyCapabilityCatalog.Version, false); }
        var host = fixture.Qualified(ModelProviderSelection.OllamaCandidate, capabilities: preferences);
        var reads = 0;
        fixture.OnVersion = () =>
        {
            reads++;
            if (late) { preferences.SetServerEnabled(KoraMcpCatalog.ServerId, false); }
        };
        CapabilityReply? observed = null;
        fixture.Adapter.OnTool = async invoke =>
            observed = await invoke(ReadOnlyCapabilityCatalog.Version, "{}", Token);
        var admission = await host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), Token);
        var result = await host.RunAsync(admission.Turn!, Token);
        observed!.Outcome.Should().Be(disabled || late ? CapabilityOutcome.Denied : CapabilityOutcome.Succeeded);
        reads.Should().Be(disabled ? 0 : 1);
        if (disabled || late) { result.Response.Should().BeNull(); }
        else { result.Outcome.Should().Be(ModelTurnOutcome.Succeeded); }
    }

    [Fact]
    public async Task Disabled_conversational_question_uses_tools_none_answer_without_presenting_a_form()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var store = new Tools.KoraMcpServerTests.MemoryStore();
        var directories = new LocalArtifactDirectoryPreferences(store);
        var preferences = new McpCapabilityPreferences(new ArtifactUsePreferences(store),
            directories, new ArtifactDirectoryConfigurationService(directories, f));
        preferences.SetToolEnabled(KoraMcpCatalog.ServerId, ModelToolProtocol.AskUserToolName, false);
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var transport = new ToolTransport(FinalAnswer);
        await using var loop = CreateLoop(f, transport);
        var host = f.ConfiguredProfiles(adapter: loop, capabilities: preferences);
        var receipt = await QuestionService(f, host).SubmitAsync(new(f.Session, null), "Original question.",
            static () => true, Token, questionPresenter: (_, _, _, _, _, _) =>
                throw new InvalidOperationException("Disabled clarification cannot present a form."));
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        transport.Requests.Should().ContainSingle().Which.ClarificationAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task Reconstructed_turn_cannot_inherit_the_issued_capability_revision()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var store = new Tools.KoraMcpServerTests.MemoryStore();
        var directories = new LocalArtifactDirectoryPreferences(store);
        var preferences = new McpCapabilityPreferences(new ArtifactUsePreferences(store),
            directories, new ArtifactDirectoryConfigurationService(directories, f));
        var host = f.Qualified(ModelProviderSelection.OllamaCandidate, capabilities: preferences);
        var issued = (await host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(), Token)).Turn!;
        var reconstructed = new ModelTurn(issued.Owner, issued.Issuer, issued.Provenance, issued.Context, issued.Wire,
            issued.SessionGeneration, issued.TaskRevision, issued.ControlRevision, issued.Registration);
        (await host.RunAsync(reconstructed, Token)).Reason.Should().Be(ModelTurnReason.HostAdmissionClosed);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Composed_external_turn_is_independent_of_builtin_tool_consideration()
    {
        using var f = new Fixture();
        using var source = new McpSource(audit: f);
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var transport = new McpModelTransport();
        await using var loop = new BoundedModelTurnAdapter(transport, f.Time, f);
        var host = f.ConfiguredProfiles(adapter: loop, capabilities: source.Capabilities);
        var approvals = new McpApprovals(f);
        var broker = new McpBroker(source.Account);
        var service = McpService(f, host, source, broker, approvals, await source.Policy());
        var receipt = await service.SubmitMcpAsync(new(f.Session, null), "Find the reviewed reference.",
            source.ServerId, source.Account, approvals.Present, McpSelect, static () => true, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        broker.Methods.Should().Equal("initialize", "tools/list", "tools/call");
    }

    [Fact]
    public async Task Disable_between_liveness_and_exact_tool_check_cannot_dispatch()
    {
        using var f = new Fixture();
        using var root = f.Root();
        using var source = new McpSource(audit: f);
        var approvals = new McpApprovals(f);
        var broker = new McpBroker(source.Account);
        await using var invocation = new McpInvocation(source.Configuration, broker, await source.Policy(),
            approvals, f, f, f, f.Time, f.Request, f.Session.Generation, source.ServerId, source.Account,
            approvals.Present, McpSelect, static () => true);
        await invocation.PrepareAsync(Token);
        var reads = 0;
        source.BeforeLoad = () =>
        {
            if (++reads != 2) { return; }
            source.BeforeLoad = null;
            source.Capabilities!.SetToolEnabled(source.ServerId, "search", false);
        };
        var revalidate = () => invocation.ValidateAsync(Token);
        (await revalidate.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should()
            .Be("The selected MCP tool is disabled.");
        broker.Methods.Should().Equal("initialize", "tools/list");
    }
}
