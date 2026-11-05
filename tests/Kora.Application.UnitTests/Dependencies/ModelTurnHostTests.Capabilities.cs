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
        var host = fixture.ConfiguredProfiles(adapter: loop);
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
}
