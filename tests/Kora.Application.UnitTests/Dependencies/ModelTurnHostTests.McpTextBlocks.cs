using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Mcp;
using Kora.Core.Authorization;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Mcp;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    private const string McpBlockText = "\n\nvalue 42\n";

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task Explicit_mcp_preserves_empty_text_blocks_in_the_actual_joined_result(RequestOrigin origin)
    {
        using var f = new Fixture(origin);
        using var source = new McpSource();
        var broker = McpTextBlockBroker(source.Account);
        var approvals = new McpApprovals(f);
        var service = McpService(f, f.Host, source, broker, approvals, await source.Policy());

        var result = await service.InvokeMcpAsync(new(f.Session, null), source.ServerId, source.Account,
            approvals.Present, McpSelect, ExplicitMcpInput, static () => true, Token, origin);

        result.Outcome.Should().Be(McpExplicitOutcome.Succeeded);
        result.DurableState.Should().Be(HostTaskState.Succeeded);
        result.Result!.Text.Should().Be(McpBlockText);
        result.Result.StructuredJson.Should().Be("""{"value":42}""");
        result.Request.Origin.Should().Be(origin);
        result.Request.SessionId.Should().Be(f.Session.SessionId);
        broker.Methods.Should().Equal("initialize", "tools/list", "tools/call");
        broker.Disposed.Should().BeTrue();
        approvals.Stages.Should().Equal(McpReviewStage.Connection, McpReviewStage.ToolSelection, McpReviewStage.Invocation);
        f.Adapter.Calls.Should().Be(0);
        f.Retained.Should().BeNull();
        f.Messages.Should().NotContain(message => message.Contains("value 42", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, true)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    public async Task Model_mcp_preserves_empty_text_blocks_through_separate_result_consent(
        RequestOrigin origin, bool consent)
    {
        using var f = new Fixture(origin);
        using var source = new McpSource();
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var model = new McpModelTransport();
        await using var loop = new BoundedModelTurnAdapter(model, f.Time, f);
        var host = f.ConfiguredProfiles(adapter: loop);
        var approvals = new McpApprovals(f)
        {
            DeniedStage = consent ? null : McpReviewStage.ResultContinuation,
        };
        var broker = McpTextBlockBroker(source.Account);
        var service = McpService(f, host, source, broker, approvals, await source.Policy());
        var reviewed = false;
        var observed = 0;
        async Task<McpApprovalPresentation> Present(HostOperationProposal proposal, HostRevision generation,
            McpReview review, Func<bool> admitted, CancellationToken token)
        {
            if (review.Stage == McpReviewStage.ResultContinuation)
            {
                reviewed = true;
                broker.Disposed.Should().BeTrue();
                model.Requests.Should().ContainSingle();
                observed.Should().Be(0);
                review.Continuation!.Result.Text.Should().Be(McpBlockText);
                review.Continuation.Result.StructuredJson.Should().Be("""{"value":42}""");
                review.Continuation.Origin.Should().Be(origin);
                review.NativeText.Should().Contain(McpBlockText);
            }
            return await approvals.Present(proposal, generation, review, admitted, token);
        }

        var receipt = await service.SubmitMcpAsync(new(f.Session, null), "Explain the exact reference.",
            source.ServerId, source.Account, Present, McpSelect, static () => true, Token, origin,
            (_, result) =>
            {
                result.Text.Should().Be(McpBlockText);
                observed++;
            });

        reviewed.Should().BeTrue();
        receipt.Message.Outcome.Should().Be(consent ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Denied);
        receipt.Task.IsTerminal.Should().BeTrue();
        broker.Methods.Should().Equal("initialize", "tools/list", "tools/call");
        broker.Disposed.Should().BeTrue();
        model.Requests.Should().HaveCount(consent ? 2 : 1);
        observed.Should().Be(consent ? 1 : 0);
        if (consent)
        {
            var resultMessage = model.Requests[1].Messages.OfType<ModelToolResultMessage>().Single();
            using var result = JsonDocument.Parse(resultMessage.Result.AsMemory());
            result.RootElement.GetProperty("mcp").GetProperty("text").GetString().Should().Be(McpBlockText);
            receipt.Message.AssistantResponse!.Body.Should().Be("The reviewed MCP reference says value 42.");
        }
        else
        {
            receipt.Message.Reason.Should().Be(ModelTurnReason.ToolResultRejected);
            receipt.Message.AssistantResponse.Should().BeNull();
        }
        f.Messages.Should().NotContain(message => message.Contains("value 42", StringComparison.Ordinal));
    }

    private static McpBroker McpTextBlockBroker(McpAccountReference account)
    {
        var response = new TaskCompletionSource<ImmutableArray<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
        response.SetResult(ImmutableArray.CreateRange(Encoding.UTF8.GetBytes(
            """{"jsonrpc":"2.0","id":3,"result":{"content":[{"type":"text","text":""},{"type":"text","text":""},{"type":"text","text":"value 42"},{"type":"text","text":""}],"structuredContent":{"value":42}}}""")));
        return new(account) { PendingCall = response };
    }
}
