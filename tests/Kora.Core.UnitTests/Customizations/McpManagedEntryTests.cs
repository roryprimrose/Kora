using System.Text;

using AwesomeAssertions;

using Kora.Core.Customizations;

namespace Kora.Core.UnitTests.Customizations;

public sealed class McpManagedEntryTests
{
    [Theory]
    [InlineData("servers", false)]
    [InlineData("mcpServers", true)]
    public void ManagedEditsPreserveEveryUnrelatedByte(string collection, bool bom)
    {
        const string selected = "{/*endpoint*/\"url\":\"https://old.invalid/mcp\", \"type\":\"http\",}";
        var source = "{\r\n// retained\r\n\"" + collection + "\":{\"example\":" + selected
            + ", \"unrelated\":{\"command\":\"unchanged\",\"env\":{\"PRIVATE\":\"not disclosed\"}},}\r\n}";
        var bytes = Encoding.UTF8.GetBytes(source);
        if (bom) { bytes = [.. Encoding.UTF8.Preamble, .. bytes]; }
        var document = new McpDefinitionDocument(bytes);
        document.ReadHttps("example").Should().Equal(Encoding.UTF8.GetBytes(selected));
        var replacement = "{\"url\":\"https://new.invalid/mcp\"}"u8.ToArray();
        var edited = document.ReplaceHttps("example", replacement);
        var expected = Encoding.UTF8.GetBytes(source.Replace(selected, Encoding.UTF8.GetString(replacement), StringComparison.Ordinal));
        if (bom) { expected = [.. Encoding.UTF8.Preamble, .. expected]; }
        edited.Should().Equal(expected);
        var added = new McpDefinitionDocument(edited).ReplaceHttps("new", "{\"url\":\"https://other.invalid/mcp\"}"u8);
        var final = new McpDefinitionDocument(added);
        final.Read("unrelated").Should().Equal(document.Read("unrelated"));
        final.ReadHttps("new").Should().Equal("{\"url\":\"https://other.invalid/mcp\"}"u8.ToArray());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("false")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("{\"url\":false}")]
    [InlineData("{\"url\":\"\"}")]
    [InlineData("{\"url\":\" https://example.invalid/mcp\"}")]
    [InlineData("{\"url\":\"not absolute\"}")]
    [InlineData("{\"url\":\"http://example.invalid\"}")]
    [InlineData("{\"url\":\"https://localhost/mcp\"}")]
    [InlineData("{\"url\":\"https://user:secret@example.invalid/mcp\"}")]
    [InlineData("{\"url\":\"https://example.invalid/mcp?token=secret\"}")]
    [InlineData("{\"url\":\"https://example.invalid/mcp#fragment\"}")]
    [InlineData("{\"url\":\"https://example.invalid/mcp\",\"type\":false}")]
    [InlineData("{\"url\":\"https://example.invalid/mcp\",\"type\":\"sse\"}")]
    [InlineData("{\"url\":\"https://example.invalid/mcp\",\"headers\":{\"Authorization\":\"secret\"}}")]
    [InlineData("{\"url\":\"https://example.invalid/mcp\",\"env\":{}}")]
    [InlineData("{\"url\":\"https://example.invalid/mcp\",\"account\":\"previous\"}")]
    [InlineData("{\"command\":\"process\",\"args\":[]}")]
    [InlineData("{\"url\":\"https://example.invalid\",\"url\":\"https://other.invalid\"}")]
    public void UnsupportedSensitiveOrMalformedEntriesCannotBeDisclosedOrReplaced(string entry)
    {
        Action validate = () => McpDefinitionDocument.ValidateHttpsServer(Encoding.UTF8.GetBytes(entry));
        validate.Should().Throw<InvalidDataException>();
        var document = new McpDefinitionDocument("{\"servers\":{}}"u8);
        Action replace = () => document.ReplaceHttps("new", Encoding.UTF8.GetBytes(entry));
        replace.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void UnsupportedExistingEntryAndRootStayInspectionOnlyWithoutDowngrade()
    {
        var original = new McpDefinitionDocument("{\"servers\":{\"example\":{\"command\":\"private\"}}}"u8);
        Action read = () => original.ReadHttps("example");
        read.Should().Throw<InvalidDataException>().Which.Message.Should().NotContain("private");
        Action replace = () => original.ReplaceHttps("example", "{\"url\":\"https://example.invalid\"}"u8);
        replace.Should().Throw<InvalidDataException>();
        var root = new McpDefinitionDocument("{\"inputs\":[],\"servers\":{}}"u8);
        Action unsupported = () => root.ReplaceHttps("new", "{\"url\":\"https://example.invalid\"}"u8);
        unsupported.Should().Throw<InvalidDataException>();
        Action missing = () => new McpDefinitionDocument("{\"servers\":{}}"u8).ReadHttps("missing");
        missing.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65537)]
    public void ExactEntryByteLimitNeverTruncates(int bytes)
    {
        Action validate = () => McpDefinitionDocument.ValidateHttpsServer(new byte[bytes]);
        validate.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void StrictUtf8AndEndpointLengthAreValidatedBeforeSourceExposure()
    {
        Action document = () => _ = new McpDefinitionDocument([.. "{\"servers\":{/*"u8, 0xff, .. "*/}}"u8]);
        document.Should().Throw<InvalidDataException>();
        Action entry = () => McpDefinitionDocument.ValidateHttpsServer([.. "{\"url\":\"https://example.invalid/"u8, 0xff, .. "\"}"u8]);
        entry.Should().Throw<InvalidDataException>();
        Action endpoint = () => McpDefinitionDocument.ValidateHttpsServer(Encoding.UTF8.GetBytes(
            "{\"url\":\"https://example.invalid/" + new string('a', 2048) + "\"}"));
        endpoint.Should().Throw<InvalidDataException>();
    }
}
