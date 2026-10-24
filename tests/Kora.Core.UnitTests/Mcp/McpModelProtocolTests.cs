using System.Text;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Mcp;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Mcp;

public sealed class McpModelProtocolTests
{
    private const string Schema = """{"type":"object","additionalProperties":false,"properties":{},"required":[]}""";
    private static readonly McpModelTool Tool = new("mcp.read", Schema);

    [Fact]
    public void Dynamic_descriptor_round_trips_only_in_its_exact_local_turn()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var envelope = Envelope(request);
        var context = ModelToolProtocol.ReadContext(envelope.Serialize(), provenance);
        context.McpTool!.Id.Should().Be(Tool.Id);
        context.McpTool.InputSchema.Should().Be(Schema);
        ModelToolProtocol.CreateRequest(provenance, null, context, [], 1).Context.Should().BeSameAs(context);
        var hosted = provenance with { Selection = ModelProviderSelection.CopilotCandidate };
        var hostedContext = ModelToolProtocol.ReadContext(envelope.Serialize(), hosted);
        FluentActions.Invoking(() => ModelToolProtocol.CreateRequest(hosted, null, hostedContext, [], 1))
            .Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new ModelContextEnvelope(request, "policy", "task", [], DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(15), ModelToolMode.McpReadOnly)).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new ModelContextEnvelope(request, "policy", "task", [], DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(15), ModelToolMode.None, Tool)).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new McpModelTool("internal.read", Schema)).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new McpModelTool(null!, Schema)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new McpModelTool("mcp.bad/name", Schema)).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Dynamic_assistant_calls_require_the_advertised_name_and_exact_schema()
    {
        var valid = """{"kind":"toolCalls","calls":[{"callId":"read_1","capabilityId":"mcp.read","input":{}}]}""";
        ModelToolProtocol.ParseAssistant(Encoding.UTF8.GetBytes(valid), mcpTool: Tool).Should().BeOfType<ModelAssistantToolCalls>();
        FluentActions.Invoking(() => ModelToolProtocol.ParseAssistant(Encoding.UTF8.GetBytes(valid)))
            .Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => ModelToolProtocol.ParseAssistant(Encoding.UTF8.GetBytes(
            valid.Replace("mcp.read", "mcp.other", StringComparison.Ordinal)), mcpTool: Tool)).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => ModelToolProtocol.ParseAssistant(Encoding.UTF8.GetBytes(
            valid.Replace("\"input\":{}", "\"input\":{\"unknown\":true}", StringComparison.Ordinal)), mcpTool: Tool))
            .Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Dynamic_results_cannot_be_smuggled_through_static_tools_or_mismatched_output_shapes()
    {
        var call = new ModelToolCall("read_1", Tool.Id, "{}");
        var reply = new CapabilityReply(CapabilityOutcome.Succeeded, "observed",
            Mcp: new(Tool.Id, new string('a', 64), DateTimeOffset.UnixEpoch, "inert untrusted text", """{"value":42}"""));
        ModelToolProtocol.CreateToolResult(call, reply, Tool).CapabilityId.Should().Be(Tool.Id);
        FluentActions.Invoking(() => ModelToolProtocol.CreateToolResult(call, reply)).Should().Throw<InvalidDataException>();
        foreach (var invalid in new[]
        {
            reply with { Mcp = null },
            reply with { Mcp = reply.Mcp! with { ToolId = "mcp.other" } },
            reply with { Mcp = reply.Mcp! with { SourceRevision = "missing" } },
            reply with { Mcp = reply.Mcp! with { SourceRevision = new string('x', 64) } },
            reply with { Mcp = reply.Mcp! with { ObservedAt = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(1)) } },
            reply with { Mcp = reply.Mcp! with { Text = "\0" } },
            reply with { Mcp = reply.Mcp! with { StructuredJson = "[]" } },
            reply with { Descriptor = ReadOnlyCapabilityCatalog.Descriptors[0] },
            reply with { Capabilities = new([], 0, null) },
            reply with { Version = new("1", "test") },
            reply with { Readiness = new([], 0, null) },
            reply with { Runtimes = new([], 0, null) },
            reply with { Runtime = new("test", RuntimeLocality.Local, CapabilityAvailability.Available, null, "test", null, false) },
            reply with { WebPage = new(HostRequest.Create(RequestOrigin.LocalUi), new("https://example.com/"),
                new("https://example.com/"), DateTimeOffset.UnixEpoch, "text", false, "text/plain") },
        })
        {
            FluentActions.Invoking(() => ModelToolProtocol.CreateToolResult(call, invalid, Tool))
                .Should().Throw<InvalidDataException>();
        }
        ModelToolProtocol.CreateToolResult(call, reply with { Mcp = reply.Mcp! with { StructuredJson = null } }, Tool)
            .Result.Should().NotBeEmpty();
        FluentActions.Invoking(() => ModelToolProtocol.CreateToolResult(new(call.CallId, "mcp.other", call.InputJson), reply, Tool))
            .Should().Throw<InvalidDataException>();
        reply.Mcp!.ToString().Should().Be("McpModelResult");
    }

    private static ModelContextEnvelope Envelope(HostRequest request) =>
        new(request, "host policy", "original user task", [], DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(15), ModelToolMode.McpReadOnly, Tool);
}
