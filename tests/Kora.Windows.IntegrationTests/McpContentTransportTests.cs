using System.Net;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Mcp;

namespace Kora.Windows.IntegrationTests;

public sealed class McpContentTransportTests
{
    [Theory]
    [InlineData(McpCapabilityKind.Resource)]
    [InlineData(McpCapabilityKind.Prompt)]
    public async Task TextPrimitiveReadsStayAtTheDelegatedEndpointWithoutToolsOrAlternateUriFetches(McpCapabilityKind kind)
    {
        const string resourceUri = "https://resource.example.com/private/document";
        using var fixture = new McpTransportFixture();
        fixture.Send = (sent, _) =>
        {
            using var request = JsonDocument.Parse(sent.Bytes);
            var root = request.RootElement;
            var method = root.GetProperty("method").GetString();
            if (string.Equals(method, "notifications/initialized", StringComparison.Ordinal))
            { return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new ByteArrayContent([]) }); }
            var result = method switch
            {
                "initialize" => "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{"
                    + (kind == McpCapabilityKind.Resource ? "\"resources\":{}" : "\"prompts\":{}")
                    + "},\"serverInfo\":{\"name\":\"fixture\",\"version\":\"1\"}}",
                "resources/read" => JsonSerializer.Serialize(new { contents = new[] { new { uri = resourceUri, text = "actual source text" } } }),
                "prompts/get" => """{"messages":[{"role":"user","content":{"type":"text","text":"actual source text"}}]}""",
                _ => throw new InvalidOperationException("The selected text operation must not invoke a tool or fetch another URI."),
            };
            return Task.FromResult(McpTransportFixture.Json("{\"jsonrpc\":\"2.0\",\"id\":"
                + root.GetProperty("id").GetRawText() + ",\"result\":" + result + "}"));
        };
        await using (var connection = await fixture.Broker.ConnectAsync(fixture.Account, TestContext.Current.CancellationToken))
        {
            var capabilities = (await connection.InitializeAsync(TestContext.Current.CancellationToken)).Value;
            capabilities.Tools.Should().BeFalse();
            capabilities.Supports(kind).Should().BeTrue();
            var document = (kind == McpCapabilityKind.Resource
                ? await connection.ReadResourceAsync(resourceUri, TestContext.Current.CancellationToken)
                : await connection.ReadPromptAsync(new(McpCapabilityKind.Prompt, "review", "Review", """{"name":"review"}"""),
                    "{}", TestContext.Current.CancellationToken)).Value;
            document.Blocks.Single().Text.Should().Be("actual source text");
        }
        fixture.Requests.Should().OnlyContain(sent => sent.Address == fixture.Account.Destination.Address);
        fixture.Resolutions.Should().Be(1);
        fixture.Methods.Should().NotContain("tools/list").And.NotContain("tools/call");
        fixture.Disposed.Should().BeTrue();
        fixture.HandlerDisposed.Should().BeTrue();
    }
}