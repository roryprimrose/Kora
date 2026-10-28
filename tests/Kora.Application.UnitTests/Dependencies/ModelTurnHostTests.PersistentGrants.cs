using AwesomeAssertions;

using Kora.Core.Authorization;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Network;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("once", 2)]
    [InlineData("session", 1)]
    [InlineData("perpetual", 1)]
    public async Task Exact_page_grants_reuse_only_the_approved_destination_and_duration(string scope, int questions)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var store = new PageApprovalStore(f) { KeepPrevious = true, Choice = scope };
        var network = new PageNetwork();
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), f.Request, ModelProviderSelection.OllamaCandidate);
        for (var index = 0; index < 2; index++)
        {
            var use = PageInvocation(f, store, network);
            (await use.InvokeAsync(provenance, WebPageCapability.Id, """{"address":"https://example.com/"}""", Token))
                .Outcome.Should().Be(Kora.Core.Tools.CapabilityOutcome.Succeeded);
            await use.ValidateAsync(Token);
        }
        store.Questions.Should().Be(questions);
        network.Sends.Should().Be(2);
        store.Snapshot!.Grants.Sum(grant => grant.UseCount).Should().Be(2);
        store.Revoke();
        (await PageInvocation(f, store, network).InvokeAsync(provenance, WebPageCapability.Id,
            """{"address":"https://example.com/"}""", Token)).Outcome.Should().Be(Kora.Core.Tools.CapabilityOutcome.Succeeded);
        store.Questions.Should().Be(questions + 1);
    }

    [Theory]
    [InlineData("session", false, 1)]
    [InlineData("session", true, 2)]
    [InlineData("perpetual", true, 1)]
    public async Task Exact_page_reuse_respects_session_identity_without_binding_always_to_original_request(
        string scope, bool differentSession, int questions)
    {
        using var f = new Fixture();
        var store = new PageApprovalStore(f) { KeepPrevious = true, Choice = scope };
        var network = new PageNetwork();
        using (var root = f.Root())
        {
            (await PageInvocation(f, store, network).InvokeAsync(
                new(new(Guid.NewGuid()), f.Request, ModelProviderSelection.OllamaCandidate), WebPageCapability.Id,
                """{"address":"https://example.com/"}""", Token)).Outcome.Should().Be(Kora.Core.Tools.CapabilityOutcome.Succeeded);
        }
        if (differentSession) { f.Session = f.Session with { SessionId = new(Guid.NewGuid()) }; }
        f.Request = new(new(Guid.NewGuid()), f.Session.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        f.Task = f.Task! with { Task = new(f.Request, f.Task.Task.Revision, f.Task.Task.State),
            Generation = f.Session.Generation };
        using (var root = f.Root())
        {
            (await PageInvocation(f, store, network).InvokeAsync(
                new(new(Guid.NewGuid()), f.Request, ModelProviderSelection.OllamaCandidate), WebPageCapability.Id,
                """{"address":"https://example.com/"}""", Token)).Outcome.Should().Be(Kora.Core.Tools.CapabilityOutcome.Succeeded);
        }
        store.Questions.Should().Be(questions);
    }

    [Theory]
    [InlineData("once", false, 6)]
    [InlineData("session", false, 3)]
    [InlineData("perpetual", false, 3)]
    [InlineData("session", true, 6)]
    [InlineData("perpetual", true, 3)]
    public async Task Exact_mcp_grants_reuse_each_separate_review_stage_without_reprompting(string scope, bool differentSession, int questions)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var approvals = new McpApprovals(f) { Choice = scope };
        var policy = await source.Policy();
        for (var index = 0; index < 2; index++)
        {
            if (index != 0)
            {
                if (differentSession) { f.Session = f.Session with { SessionId = new(Guid.NewGuid()) }; }
                f.Request = new(new(Guid.NewGuid()), f.Session.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
                f.Task = f.Task! with { Task = new(f.Request, new(1), HostTaskState.IntentRecorded),
                    Generation = f.Session.Generation };
            }
            using var root = f.Root();
            var broker = new McpBroker(source.Account);
            await using var use = McpUse(f, source, broker, approvals, policy);
            await use.PrepareAsync(Token);
            var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), f.Request, ModelProviderSelection.OllamaCandidate);
            (await use.InvokeAsync(provenance, "mcp.search", """{"query":"reference"}""", Token))
                .Outcome.Should().Be(Kora.Core.Tools.CapabilityOutcome.Succeeded);
            broker.Methods.Should().Equal("initialize", "tools/list", "tools/call");
        }
        approvals.Questions.Should().Be(questions);
    }

    [Fact]
    public async Task AlwaysMcpArgumentsRemainExactWithoutRevokingOtherApprovedArgumentSets()
    {
        using var f = new Fixture();
        using var root = f.Root();
        using var source = new McpSource();
        var approvals = new McpApprovals(f) { Choice = "perpetual" };
        var policy = await source.Policy();
        foreach (var query in new[] { "reference", "other", "reference" })
        {
            var broker = new McpBroker(source.Account);
            await using var use = McpUse(f, source, broker, approvals, policy);
            await use.PrepareAsync(Token);
            (await use.InvokeAsync(new(new(Guid.NewGuid()), f.Request, ModelProviderSelection.OllamaCandidate),
                "mcp.search", "{\"query\":\"" + query + "\"}", Token)).Outcome
                .Should().Be(Kora.Core.Tools.CapabilityOutcome.Succeeded);
        }
        approvals.Questions.Should().Be(4);
    }

    [Fact]
    public async Task AlwaysPageDestinationsRemainIndependentExactPermissions()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var store = new PageApprovalStore(f) { KeepPrevious = true, Choice = "perpetual" };
        var network = new PageNetwork();
        foreach (var address in new[] { "https://example.com/", "https://second.example/", "https://example.com/" })
        {
            var use = new Kora.Application.Network.LocalWebPageInvocation(new(network, new PageAudit(f)),
                store, store.Present, f.Request, f.Session.Generation, new(address),
                f.Time.Now.AddSeconds(15), () => f.CanControl, f.Time);
            (await use.InvokeAsync(new(new(Guid.NewGuid()), f.Request, ModelProviderSelection.OllamaCandidate),
                WebPageCapability.Id, "{\"address\":\"" + address + "\"}", Token)).Outcome
                .Should().Be(Kora.Core.Tools.CapabilityOutcome.Succeeded);
        }
        store.Questions.Should().Be(2);
        store.Snapshot!.Grants.Should().HaveCount(2).And.OnlyContain(grant => grant.Status == OperationGrantStatus.Active);
        network.Sends.Should().Be(3);
    }
}
