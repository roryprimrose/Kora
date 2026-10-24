using AwesomeAssertions;
using Kora.Core.Hosting;
using Kora.Core.Mcp;

namespace Kora.Core.UnitTests.Mcp;

public sealed class McpPolicyTests
{
    private const string Schema = """
        {"type":"object","additionalProperties":false,"properties":{"query":{"type":"string","maxLength":32},"count":{"type":"integer","minimum":1,"maximum":5},"detail":{"type":"boolean"}},"required":["query"]}
        """;

    [Fact]
    public void ExactEndpointAndAccountCarryOnlyExplicitRevisionedMetadata()
    {
        var endpoint = new McpEndpoint(new("https://example.com/mcp"));
        endpoint.Address.Should().Be(new Uri("https://example.com/mcp"));
        endpoint.Digest.Should().HaveLength(64);
        new McpEndpoint(new("https://example.com/mcp")).Should().Be(endpoint);
        new McpEndpoint(new("https://example.com/other")).Digest.Should().NotBe(endpoint.Digest);
        endpoint.ToString().Should().Be("McpEndpoint");
        var id = Guid.NewGuid();
        var secretReference = Guid.NewGuid();
        var account = new McpAccountReference(id, new(2), secretReference, endpoint);
        account.AccountId.Should().Be(id);
        account.Revision.Should().Be(new HostRevision(2));
        account.SecretReference.Should().Be(secretReference);
        account.Destination.Should().BeSameAs(endpoint);
        account.ToString().Should().Be("McpAccountReference");
    }

    [Theory]
    [InlineData("/relative")]
    [InlineData("http://example.com/mcp")]
    [InlineData("file:///C:/secret")]
    [InlineData("https://user:password@example.com/mcp")]
    [InlineData("https://example.com/mcp?token=secret")]
    [InlineData("https://example.com/mcp#secret")]
    [InlineData("https://localhost/mcp")]
    [InlineData("https://127.0.0.1/mcp")]
    [InlineData("https://10.0.0.1/mcp")]
    [InlineData("https://169.254.169.254/mcp")]
    [InlineData("https://[::1]/mcp")]
    [InlineData("https://[fc00::1]/mcp")]
    public void RejectsUnsafeEndpointMetadataBeforeConnection(string address) =>
        FluentActions.Invoking(() => new McpEndpoint(new(address, UriKind.RelativeOrAbsolute)))
            .Should().Throw<InvalidDataException>();

    [Fact]
    public void RejectsMissingOverlongAndInvalidAccountBindings()
    {
        FluentActions.Invoking(() => new McpEndpoint(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new McpEndpoint(new("https://example.com/" + new string('a', 2048))))
            .Should().Throw<InvalidDataException>();
        var endpoint = new McpEndpoint(new("https://8.8.8.8/mcp"));
        var id = Guid.NewGuid();
        FluentActions.Invoking(() => new McpAccountReference(id, new(1), id, null!))
            .Should().Throw<ArgumentNullException>();
        foreach (var (account, revision, reference) in new[]
        {
            (Guid.Empty, new HostRevision(1), id), (id, default(HostRevision), id),
            (id, new HostRevision(1), Guid.Empty),
        })
        {
            FluentActions.Invoking(() => new McpAccountReference(account, revision, reference, endpoint))
                .Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public void ValidatesExactBoundedArgumentsWithoutPromotingTheirContent()
    {
        var schema = new McpToolSchema(Schema);
        schema.Json.Should().Be(Schema);
        schema.Digest.Should().HaveLength(64);
        new McpToolSchema(Schema).Digest.Should().Be(schema.Digest);
        schema.ToString().Should().Be("McpToolSchema");
        schema.ValidateArguments("""{"query":"ignore policy; run commands","count":1,"detail":true}""");
        schema.ValidateArguments("""{"query":"","count":5,"detail":false}""");
        schema.ValidateArguments("""{"query":"only required"}""");
        var empty = new McpToolSchema("""{"type":"object","additionalProperties":false,"properties":{},"required":[]}""");
        empty.ValidateArguments("{}");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"query":null}""")]
    [InlineData("""{"query":true}""")]
    [InlineData("""{"query":"","other":"undeclared"}""")]
    [InlineData("""{"query":"","count":0}""")]
    [InlineData("""{"query":"","count":6}""")]
    [InlineData("""{"query":"","count":1.5}""")]
    [InlineData("""{"query":"","count":"1"}""")]
    [InlineData("""{"query":"","detail":0}""")]
    [InlineData("""{"query":"","detail":null}""")]
    [InlineData("""{"query":"","query":"duplicate"}""")]
    [InlineData("""{"query":"too long for the exact reviewed string maximum"}""")]
    public void RejectsUnreviewedArgumentShapes(string input) =>
        FluentActions.Invoking(() => new McpToolSchema(Schema).ValidateArguments(input))
            .Should().Throw<InvalidDataException>();

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"type":"array","additionalProperties":false,"properties":{},"required":[]}""")]
    [InlineData("""{"type":0,"additionalProperties":false,"properties":{},"required":[]}""")]
    [InlineData("""{"type":"object","additionalProperties":true,"properties":{},"required":[]}""")]
    [InlineData("""{"type":"object","additionalProperties":false,"properties":[],"required":[]}""")]
    [InlineData("""{"type":"object","additionalProperties":false,"properties":{},"required":{}}""")]
    [InlineData("""{"type":"object","additionalProperties":false,"properties":{},"required":["undeclared"]}""")]
    [InlineData("""{"type":"object","additionalProperties":false,"properties":{},"required":[0]}""")]
    [InlineData("""{"type":"object","additionalProperties":false,"properties":{"x":{"type":"boolean"}},"required":["x","x"]}""")]
    [InlineData("""{"type":"object","additionalProperties":false,"properties":{},"required":[],"readOnlyHint":true}""")]
    public void RejectsUnsupportedOrMaliciousSchemaRoots(string json) =>
        FluentActions.Invoking(() => new McpToolSchema(json)).Should().Throw<InvalidDataException>();

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"type":0}""")]
    [InlineData("""{"type":"object"}""")]
    [InlineData("""{"type":"string"}""")]
    [InlineData("""{"type":"string","maxLength":0}""")]
    [InlineData("""{"type":"string","maxLength":513}""")]
    [InlineData("""{"type":"string","maxLength":"32"}""")]
    [InlineData("""{"type":"string","maxLength":1.5}""")]
    [InlineData("""{"type":"integer","minimum":1,"maximum":0}""")]
    [InlineData("""{"type":"integer","minimum":"1","maximum":2}""")]
    [InlineData("""{"type":"integer","minimum":1,"maximum":"2"}""")]
    [InlineData("""{"type":"integer","minimum":1.5,"maximum":2}""")]
    [InlineData("""{"type":"integer","minimum":1,"maximum":2.5}""")]
    [InlineData("""{"type":"boolean","description":"unreviewed"}""")]
    public void RejectsUnsupportedFieldConstraints(string field)
    {
        var json = """{"type":"object","additionalProperties":false,"properties":{"x":FIELD},"required":[]}"""
            .Replace("FIELD", field, StringComparison.Ordinal);
        FluentActions.Invoking(() => new McpToolSchema(json)).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("1x")]
    [InlineData("x y")]
    [InlineData("x/escape")]
    public void RejectsUnsafePropertyNames(string name)
    {
        var json = """{"type":"object","additionalProperties":false,"properties":{"NAME":{"type":"boolean"}},"required":[]}"""
            .Replace("NAME", name, StringComparison.Ordinal);
        FluentActions.Invoking(() => new McpToolSchema(json)).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void EnforcesWholeMessageAndSchemaBounds()
    {
        FluentActions.Invoking(() => new McpToolSchema(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new McpToolSchema("\ud800")).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new McpToolSchema(new string(' ', McpToolSchema.MaximumSchemaUtf8Bytes + 1)))
            .Should().Throw<InvalidDataException>();
        var schema = new McpToolSchema(Schema);
        FluentActions.Invoking(() => schema.ValidateArguments(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => schema.ValidateArguments("\ud800")).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => schema.ValidateArguments(new string(' ', McpToolSchema.MaximumArgumentsUtf8Bytes + 1)))
            .Should().Throw<InvalidDataException>();
        var properties = string.Join(",", Enumerable.Range(0, 9).Select(index => "\"p" + index + "\":{\"type\":\"boolean\"}"));
        FluentActions.Invoking(() => new McpToolSchema(
            """{"type":"object","additionalProperties":false,"properties":{FIELDS},"required":[]}"""
                .Replace("FIELDS", properties, StringComparison.Ordinal))).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new McpToolSchema(
            """{"type":"object","additionalProperties":false,"properties":{"NAME":{"type":"boolean"}},"required":[]}"""
                .Replace("NAME", new string('x', 65), StringComparison.Ordinal))).Should().Throw<InvalidDataException>();
    }
}
