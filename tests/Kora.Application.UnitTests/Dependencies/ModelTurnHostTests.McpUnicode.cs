using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Mcp;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Mcp;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.LocalUi, true)]
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    public async Task Explicit_mcp_unicode_arguments_complete_after_exact_review_without_model_use(RequestOrigin origin, bool escaped)
    {
        using var f = new Fixture(origin);
        using var source = new McpSource();
        var broker = new McpBroker(source.Account);
        var approvals = new McpApprovals(f);
        var service = McpService(f, f.Host, source, broker, approvals, await source.Policy());
        var text = string.Concat(Enumerable.Repeat("\U00020000", 32));
        var arguments = escaped ? JsonSerializer.Serialize(new { query = text }) : "{\"query\":\"" + text + "\"}";
        Task<string?> Input(HostRequest request, HostRevision generation, DateTimeOffset expires,
            McpModelTool tool, Func<bool> admitted, CancellationToken token) => Task.FromResult<string?>(arguments);

        var result = await service.InvokeMcpAsync(new(f.Session, null), source.ServerId, source.Account,
            approvals.Present, McpSelect, Input, static () => true, Token, origin);

        result.Outcome.Should().Be(McpExplicitOutcome.Succeeded);
        result.DurableState.Should().Be(HostTaskState.Succeeded);
        result.Request.Origin.Should().Be(origin);
        result.Result!.Text.Should().Contain("value 42");
        approvals.Stages.Should().Equal(McpReviewStage.Connection, McpReviewStage.ToolSelection, McpReviewStage.Invocation);
        approvals.InvocationReview!.Arguments.Should().Be(arguments);
        broker.InvocationArguments.Should().Be(arguments);
        broker.Methods.Should().Equal("initialize", "tools/list", "tools/call");
        broker.Disposed.Should().BeTrue();
        f.Adapter.Calls.Should().Be(0);
        f.Retained.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_mcp_unicode_arguments_over_scalar_limit_never_request_invocation_approval_or_send(bool escaped)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var broker = new McpBroker(source.Account);
        var approvals = new McpApprovals(f);
        var service = McpService(f, f.Host, source, broker, approvals, await source.Policy());
        var text = string.Concat(Enumerable.Repeat("\U00020000", 33));
        var arguments = escaped ? JsonSerializer.Serialize(new { query = text }) : "{\"query\":\"" + text + "\"}";
        Task<string?> Input(HostRequest request, HostRevision generation, DateTimeOffset expires,
            McpModelTool tool, Func<bool> admitted, CancellationToken token) => Task.FromResult<string?>(arguments);

        var result = await service.InvokeMcpAsync(new(f.Session, null), source.ServerId, source.Account,
            approvals.Present, McpSelect, Input, static () => true, Token);

        result.Outcome.Should().Be(McpExplicitOutcome.Denied);
        result.DurableState.Should().Be(HostTaskState.Unknown);
        result.Result.Should().BeNull();
        approvals.Stages.Should().Equal(McpReviewStage.Connection, McpReviewStage.ToolSelection);
        broker.Methods.Should().Equal("initialize", "tools/list");
        broker.InvocationArguments.Should().BeNull();
        broker.Disposed.Should().BeTrue();
        f.Adapter.Calls.Should().Be(0);
        f.Retained.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Local_model_mcp_unicode_arguments_keep_exact_invocation_and_separate_result_consent(bool escaped)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var text = string.Concat(Enumerable.Repeat("\U00020000", 32));
        var arguments = escaped ? JsonSerializer.Serialize(new { query = text }) : "{\"query\":\"" + text + "\"}";
        var model = new McpModelTransport { Arguments = arguments };
        await using var loop = new BoundedModelTurnAdapter(model, f.Time, f);
        var broker = new McpBroker(source.Account);
        var approvals = new McpApprovals(f);
        var service = McpService(f, f.ConfiguredProfiles(adapter: loop), source, broker, approvals, await source.Policy());

        var receipt = await service.SubmitMcpAsync(new(f.Session, null), "Find the reviewed reference.",
            source.ServerId, source.Account, approvals.Present, McpSelect, static () => true, Token);

        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse!.Body.Should().Be("The reviewed MCP reference says value 42.");
        approvals.Stages.Should().Equal(McpReviewStage.Connection, McpReviewStage.ToolSelection,
            McpReviewStage.Invocation, McpReviewStage.ResultContinuation);
        approvals.InvocationReview!.Arguments.Should().Be(arguments);
        broker.InvocationArguments.Should().Be(arguments);
        broker.Methods.Should().Equal("initialize", "tools/list", "tools/call");
        broker.Disposed.Should().BeTrue();
        model.Requests.Should().HaveCount(2);
    }
}
