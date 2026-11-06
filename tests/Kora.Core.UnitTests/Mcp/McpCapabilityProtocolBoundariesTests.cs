using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Mcp;

namespace Kora.Core.UnitTests.Mcp;

public sealed class McpCapabilityProtocolBoundariesTests
{
    [Theory]
    [InlineData("""{"protocolVersion":"other","capabilities":{"tools":{}},"serverInfo":{"name":"x","version":"1"}}""")]
    [InlineData("""{"protocolVersion":"2025-06-18","capabilities":[],"serverInfo":{"name":"x","version":"1"}}""")]
    [InlineData("""{"protocolVersion":"2025-06-18","capabilities":{"tools":{}},"serverInfo":null}""")]
    [InlineData("""{"protocolVersion":"2025-06-18","capabilities":{},"serverInfo":{"name":"x","version":"1"}}""")]
    [InlineData("""{"protocolVersion":"2025-06-18","capabilities":{"tools":false},"serverInfo":{"name":"x","version":"1"}}""")]
    public void InvalidNegotiationNeverEstablishesAnInventory(string result) =>
        FluentActions.Invoking(() => McpCapabilityProtocol.ReadInitialization(Rpc(result, 1))).Should().Throw<InvalidDataException>();

    [Theory]
    [InlineData("""{"tools":[null]}""")]
    [InlineData("""{"tools":[{}]}""")]
    [InlineData("""[]""")]
    [InlineData("""{"tools":[],"extra":true}""")]
    [InlineData("""{"tools":[{"name":""}]}""")]
    [InlineData("""{"tools":[{"name":true}]}""")]
    [InlineData("""{"tools":[{"name":"a\nb"}]}""")]
    [InlineData("""{"tools":[{"name":"x","description":null}]}""")]
    [InlineData("""{"tools":[{"name":"x","description":""}]}""")]
    [InlineData("""{"tools":[]},"extra":true""")]
    [InlineData("""{"tools":[],"nextCursor":""}""")]
    [InlineData("""{"tools":[],"nextCursor":true}""")]
    public void InvalidPageMetadataIsNotSilentlyDropped(string result) =>
        FluentActions.Invoking(() => McpCapabilityProtocol.ReadPage(Rpc(result), McpCapabilityKind.Tool, 2))
            .Should().Throw<InvalidDataException>();

    [Fact]
    public void PromptContentMustDeclareAnObjectAndItsExactContentType()
    {
        foreach (var content in new[] { "null", "{}" })
        {
            FluentActions.Invoking(() => McpCapabilityProtocol.ReadPrompt(
                Rpc("{\"messages\":[{\"role\":\"user\",\"content\":" + content + "}]}"), 2))
                .Should().Throw<InvalidDataException>();
        }
    }

    [Theory]
    [InlineData(McpCapabilityKind.Tool, "tools", """{"name":"x"}""")]
    [InlineData(McpCapabilityKind.Resource, "resources", """{"name":"x","uri":"relative"}""")]
    [InlineData(McpCapabilityKind.ResourceTemplate, "resourceTemplates", """{"name":"x","uriTemplate":"data://item/{"}""")]
    [InlineData(McpCapabilityKind.Prompt, "prompts", """{"name":"x","arguments":null}""")]
    [InlineData(McpCapabilityKind.Prompt, "prompts", """{"name":"x","arguments":[{"name":"a","required":"true"}]}""")]
    [InlineData(McpCapabilityKind.Prompt, "prompts", """{"name":"x","arguments":[{"name":"a"},{"name":"a"}]}""")]
    public void UnsupportedDefinitionsRemainInspectableWithoutConveyingUseAuthority(McpCapabilityKind kind, string property, string entry)
    {
        var page = McpCapabilityProtocol.ReadPage(Rpc("{\"" + property + "\":[" + entry + "]}"), kind, 2);
        page.Items.Single().UnavailableReason.Should().NotBeNull();
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/json")]
    [InlineData("application/xml")]
    [InlineData("application/example+json")]
    [InlineData("application/example+xml")]
    public void TextMimeTypesAreExplicitlyAdmitted(string mime)
    {
        var document = McpCapabilityProtocol.ReadResource(Rpc(JsonSerializer.Serialize(new
        {
            contents = new[] { new { uri = "data://item", mimeType = mime, text = string.Empty } },
        })), 2, "data://item");
        document.Blocks.Single().MimeType.Should().Be(mime);
        document.Blocks.Single().Text.Should().BeEmpty();
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("audio/wav")]
    [InlineData("application/octet-stream")]
    public void BinaryMimeTypesCannotMasqueradeAsText(string mime) =>
        FluentActions.Invoking(() => McpCapabilityProtocol.ReadResource(Rpc(JsonSerializer.Serialize(new
        {
            contents = new[] { new { uri = "data://item", mimeType = mime, text = "base64-looking data" } },
        })), 2, "data://item")).Should().Throw<InvalidDataException>();

    [Fact]
    public void ProtocolFieldAndWholeMessageBoundsRejectOverflowAndUnknownKinds()
    {
        foreach (var result in new[]
        {
            "{\"tools\":[{\"name\":\"" + new string('a', 129) + "\"}]}",
            "{\"tools\":[{\"name\":\"x\",\"description\":\"" + new string('a', 2049) + "\"}]}",
            "{\"tools\":[" + string.Join(",", Enumerable.Range(0, 257).Select(index => "{\"name\":\"t" + index + "\"}")) + "]}",
        })
        { FluentActions.Invoking(() => McpCapabilityProtocol.ReadPage(Rpc(result), McpCapabilityKind.Tool, 2)).Should().Throw<InvalidDataException>(); }
        foreach (var cursor in new[] { "", new string('a', 2049), "control\n" })
        { FluentActions.Invoking(() => McpCapabilityProtocol.ListRequest(McpCapabilityKind.Tool, 2, cursor)).Should().Throw<InvalidDataException>(); }
        FluentActions.Invoking(() => McpCapabilityProtocol.ReadPage(Rpc("""{"tools":[]}"""), (McpCapabilityKind)99, 2))
            .Should().Throw<ArgumentOutOfRangeException>();
        foreach (var kind in Enum.GetValues<McpCapabilityKind>())
        { new McpServerCapabilities(false, false, false).Supports(kind).Should().BeFalse(); }
        var hugePrompt = new McpCapability(McpCapabilityKind.Prompt, new string('a', 17000), "x", """{"name":"x"}""");
        FluentActions.Invoking(() => McpCapabilityProtocol.PromptRequest(hugePrompt, "{}", 2)).Should().Throw<InvalidDataException>();
        var tooMany = new McpCapability(McpCapabilityKind.Prompt, "x", "x", "{\"name\":\"x\",\"arguments\":["
            + string.Join(",", Enumerable.Range(0, 9).Select(index => "{\"name\":\"p" + index + "\"}")) + "]}");
        FluentActions.Invoking(() => McpCapabilityProtocol.PromptRequest(tooMany, "{}", 2)).Should().Throw<InvalidDataException>();
        var unsupported = tooMany with { UnavailableReason = "unsupported" };
        FluentActions.Invoking(() => McpCapabilityProtocol.PromptRequest(unsupported, "{}", 2)).Should().Throw<InvalidDataException>();
        using var request = JsonDocument.Parse(McpCapabilityProtocol.PromptRequest(
            new(McpCapabilityKind.Prompt, "x", "x", """{"name":"x","arguments":[{"name":"optional","required":false}]}"""), "{}", 2));
        request.RootElement.GetProperty("params").GetProperty("arguments").GetRawText().Should().Be("{}");
    }

    [Fact]
    public void MalformedNumericCorrelationAndServerErrorsRemainTypedAndPrivate()
    {
        foreach (var wire in new[]
        {
            """{"jsonrpc":"2.0","id":2.5,"result":{}}""",
            """{"jsonrpc":"2.0","id":9999999999999999999999,"result":{}}""",
            """{"jsonrpc":"2.0","id":2}""",
            """{"jsonrpc":"2.0","id":2,"error":{"code":1.5,"message":"error"}}""",
            """{"jsonrpc":"2.0","id":2,"error":{"code":9999999999999999,"message":"error"}}""",
            """{"jsonrpc":"2.0","id":2,"error":{"code":-1,"message":null}}""",
        })
        { FluentActions.Invoking(() => McpCapabilityProtocol.ReadPage(Encoding.UTF8.GetBytes(wire), McpCapabilityKind.Tool, 2)).Should().Throw<InvalidDataException>(); }
        var error = FluentActions.Invoking(() => McpCapabilityProtocol.ReadPage(
            """{"jsonrpc":"2.0","id":2,"error":{"code":-32002,"message":"private detail","data":{"private":"value"}}}"""u8,
            McpCapabilityKind.Tool, 2)).Should().Throw<McpProtocolException>().Which;
        error.Code.Should().Be(-32002);
        error.ToString().Should().NotContain("private detail").And.NotContain("private\":\"value");
    }

    [Fact]
    public void CompleteTextDocumentRejectsInvalidBlocksAndSerializedOverflow()
    {
        new McpTextBlock("private source", "data://item", "text/plain", "user").ToString().Should().Be(nameof(McpTextBlock));
        FluentActions.Invoking(() => new McpTextDocument(null!)).Should().Throw<ArgumentNullException>();
        foreach (var blocks in new IEnumerable<McpTextBlock>[]
        {
            [], [null!], [new(null!, null, null, null)], [new("text", null, null, "system")],
            Enumerable.Repeat(new McpTextBlock("text", null, null, null), 17),
            [new(new string('a', McpCapabilityProtocol.MaximumContentBytes), null, null, null)],
        })
        { FluentActions.Invoking(() => new McpTextDocument(blocks)).Should().Throw<InvalidDataException>(); }
    }

    private static byte[] Rpc(string result, int id = 2) => McpCapabilityProtocolTests.Rpc(result, id);
}