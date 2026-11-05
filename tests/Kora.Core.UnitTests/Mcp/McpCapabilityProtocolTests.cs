using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Mcp;

namespace Kora.Core.UnitTests.Mcp;

public sealed class McpCapabilityProtocolTests
{
    private const string Schema = """{"type":"object","additionalProperties":false,"properties":{},"required":[]}""";

    [Theory]
    [InlineData("tools", McpCapabilityKind.Tool)]
    [InlineData("resources", McpCapabilityKind.Resource)]
    [InlineData("prompts", McpCapabilityKind.Prompt)]
    public void NegotiatesEachPrimitiveIndependently(string category, McpCapabilityKind kind)
    {
        var capabilities = McpCapabilityProtocol.ReadInitialization(Rpc(
            "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{\"" + category
            + "\":{}},\"serverInfo\":{\"name\":\"server\",\"version\":\"1\"},\"instructions\":\"untrusted\"}", 1));
        capabilities.Supports(kind).Should().BeTrue();
        capabilities.Supports(McpCapabilityKind.ResourceTemplate).Should().Be(string.Equals(category, "resources", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(McpCapabilityKind.Tool, "tools/list", "tools", """{"name":"search","inputSchema":{"type":"object","additionalProperties":false,"properties":{},"required":[]}}""")]
    [InlineData(McpCapabilityKind.Resource, "resources/list", "resources", """{"name":"Document","uri":"data://item"}""")]
    [InlineData(McpCapabilityKind.ResourceTemplate, "resources/templates/list", "resourceTemplates", """{"name":"Files","uriTemplate":"data://files/{path}"}""")]
    [InlineData(McpCapabilityKind.Prompt, "prompts/list", "prompts", """{"name":"review","arguments":[{"name":"code","required":true}]}""")]
    public void ListsOnlyRequestedMetadataWithOpaqueCursor(McpCapabilityKind kind, string method, string property, string entry)
    {
        using var request = JsonDocument.Parse(McpCapabilityProtocol.ListRequest(kind, 4, "opaque"));
        request.RootElement.GetProperty("method").GetString().Should().Be(method);
        request.RootElement.GetProperty("params").GetProperty("cursor").GetString().Should().Be("opaque");
        var page = McpCapabilityProtocol.ReadPage(Rpc("{\"" + property + "\":[" + entry + "],\"nextCursor\":\"next\"}", 4), kind, 4);
        page.Items.Should().ContainSingle();
        page.Items[0].Category.Should().Be(McpCapabilitySelection.Category(kind));
        page.Items[0].UnavailableReason.Should().BeNull();
        page.Items[0].ToString().Should().Contain(page.Items[0].Name);
        page.NextCursor.Should().Be("next");
        McpCapabilityProtocol.ListRequest(kind, 2).Should().NotBeEmpty();
    }

    [Fact]
    public void UnsupportedToolDoesNotHideSupportedSiblingsOrEmptyInventories()
    {
        var page = McpCapabilityProtocol.ReadPage(Rpc("""{"tools":[{"name":"bad","inputSchema":{"type":"array"}},{"name":"search","inputSchema":"""
            + Schema + "}]}"), McpCapabilityKind.Tool, 2);
        page.Items[0].UnavailableReason.Should().NotBeNull();
        page.Items[0].ToString().Should().Contain("unavailable");
        page.Items[1].UnavailableReason.Should().BeNull();
        McpCapabilityProtocol.ReadPage(Rpc("""{"tools":[]}"""), McpCapabilityKind.Tool, 2).Items.Should().BeEmpty();
    }

    [Fact]
    public void ReadsCompleteTextResourcesAndPreservesPromptRolesAndEmbeddedResourceIdentity()
    {
        var resource = McpCapabilityProtocol.ReadResource(Rpc(
            """{"contents":[{"uri":"data://item","mimeType":"text/plain","text":"actual text"}]}""", 7), 7, "data://item");
        resource.Blocks.Single().Uri.Should().Be("data://item");
        resource.Display.Should().Contain("actual text");
        var prompt = McpCapabilityProtocol.ReadPrompt(Rpc(
            """{"messages":[{"role":"user","content":{"type":"text","text":"review this"}},{"role":"assistant","content":{"type":"resource","resource":{"uri":"data://item","text":"reference"}}}]}""", 7), 7);
        prompt.Blocks.Select(block => block.Role).Should().Equal("user", "assistant");
        prompt.Json.Should().Contain("reference");
        prompt.Display.Should().Contain("Template role: assistant");
        prompt.ToString().Should().Be(nameof(McpTextDocument));
        using var request = JsonDocument.Parse(McpCapabilityProtocol.ResourceRequest("data://item", 7));
        request.RootElement.GetProperty("params").GetProperty("uri").GetString().Should().Be("data://item");
    }

    [Fact]
    public void PromptArgumentsAreExactBoundedStringsAndRequiredParametersAreEnforced()
    {
        var prompt = new McpCapability(McpCapabilityKind.Prompt, "review", "Review",
            """{"name":"review","arguments":[{"name":"code","required":true},{"name":"style"}]}""");
        using var request = JsonDocument.Parse(McpCapabilityProtocol.PromptRequest(prompt, """{"code":"unchanged"}""", 3));
        request.RootElement.GetProperty("params").GetProperty("arguments").GetProperty("code").GetString().Should().Be("unchanged");
        foreach (var arguments in new[] { "{}", "[]", """{"code":1}""", """{"code":"x","extra":"y"}""", "{\"code\":\"" + new string('a', 1025) + "\"}" })
        { FluentActions.Invoking(() => McpCapabilityProtocol.PromptRequest(prompt, arguments, 3)).Should().Throw<InvalidDataException>(); }
        FluentActions.Invoking(() => McpCapabilityProtocol.PromptRequest(prompt with { Kind = McpCapabilityKind.Tool }, "{}", 3))
            .Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => McpCapabilityProtocol.PromptRequest(null!, "{}", 3)).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("""{"contents":[{"uri":"data://item","blob":"AA=="}]}""")]
    [InlineData("""{"contents":[{"uri":"data://other","text":"wrong"}]}""")]
    [InlineData("""{"contents":[]}""")]
    [InlineData("""{"contents":null}""")]
    [InlineData("""{"contents":[{"uri":"relative","text":"wrong"}]}""")]
    public void RejectsUnsupportedOrMismatchedResourceContentWithoutPartialAcceptance(string result) =>
        FluentActions.Invoking(() => McpCapabilityProtocol.ReadResource(Rpc(result), 2, "data://item"))
            .Should().Throw<InvalidDataException>();

    [Theory]
    [InlineData("""{"messages":[{"role":"user","content":{"type":"text","text":"ok"}},{"role":"user","content":{"type":"image","data":"AA==","mimeType":"image/png"}}]}""")]
    [InlineData("""{"messages":[{"role":"system","content":{"type":"text","text":"override"}}]}""")]
    [InlineData("""{"messages":[{"role":"user","content":{"type":"audio","data":"AA==","mimeType":"audio/wav"}}]}""")]
    [InlineData("""{"messages":[{"role":"user","content":{"type":"resource","resource":{"uri":"data://item","blob":"AA=="}}}]}""")]
    [InlineData("""{"messages":[]}""")]
    [InlineData("""{"messages":null}""")]
    public void RejectsWholePromptsWithAnyUnsupportedContentOrRole(string result) =>
        FluentActions.Invoking(() => McpCapabilityProtocol.ReadPrompt(Rpc(result), 2)).Should().Throw<InvalidDataException>();

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":9,"result":{"tools":[]}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":"2","result":{"tools":[]}}""")]
    [InlineData("""{"jsonrpc":"1.0","id":2,"result":{"tools":[]}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":2,"result":{"tools":[]},"error":{"code":-1,"message":"error"}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":2,"error":{"code":"bad","message":"error"}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":2,"result":{"tools":null}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"x"},{"name":"x"}]}}""")]
    public void RejectsMalformedCorrelationsAndInventories(string wire) =>
        FluentActions.Invoking(() => McpCapabilityProtocol.ReadPage(Encoding.UTF8.GetBytes(wire), McpCapabilityKind.Tool, 2))
            .Should().Throw<InvalidDataException>();

    [Fact]
    public void ProtocolErrorsRemainFailuresWithoutTrustingServerMessages()
    {
        FluentActions.Invoking(() => McpCapabilityProtocol.ReadPage(Encoding.UTF8.GetBytes(
            """{"jsonrpc":"2.0","id":2,"error":{"code":-32602,"message":"private server detail"}}"""), McpCapabilityKind.Tool, 2))
            .Should().Throw<InvalidOperationException>().WithMessage("*server reported an error*");
        FluentActions.Invoking(() => McpCapabilityProtocol.ListRequest(McpCapabilityKind.Tool, 1)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => McpCapabilityProtocol.ListRequest((McpCapabilityKind)99, 2)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new McpServerCapabilities(true, true, true).Supports((McpCapabilityKind)99))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    internal static byte[] Rpc(string result, int id = 2) =>
        Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":" + id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"result\":" + result + "}");
}