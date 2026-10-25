using System.Text;

using AwesomeAssertions;

using Kora.Core.Agents;

namespace Kora.Core.UnitTests.Agents;

public sealed class OwnedAgentProfileDocumentTests
{
    private const string Source = """
        ---
        name: example
        description: Original tool-free profile
        kora-agent-format: 1
        runtime: local
        input: text
        output: text
        references: none
        tools: none
        maximum-calls: 0
        maximum-rounds: 4
        maximum-input-bytes: 2147483647
        maximum-output-bytes: 2147483647
        deadline-ms: 2147483647
        ---
        Explain the user's text without external resources.
        """;

    [Fact]
    public void ExactBytesAndSupportedV1ProfileAreRetainedWithoutInvocation()
    {
        var bytes = Encoding.UTF8.GetBytes(Source);
        var saved = OwnedAgentProfileDocument.Parse("example", bytes);
        saved.Name.Should().Be("example");
        saved.Document.CopyBytes().Should().Equal(bytes);
        saved.Profile.Tools.Should().BeEmpty();
        saved.Profile.MaximumCalls.Should().Be(0);
        saved.Profile.MaximumRounds.Should().Be(1);
        saved.Profile.MaximumInputUtf8Bytes.Should().BeLessThan(int.MaxValue);
        saved.Profile.MaximumOutputUtf8Bytes.Should().BeLessThan(int.MaxValue);
        saved.Profile.Deadline.TotalMilliseconds.Should().BeLessThan(int.MaxValue);
        saved.Document.Definition.Source.Should().Be("kora-profile");
        bytes[0] = 0;
        saved.Document.CopyBytes()[0].Should().Be((byte)'-');
    }

    [Theory]
    [InlineData("../example")]
    [InlineData("Example")]
    [InlineData("con")]
    [InlineData("example.agent.md")]
    [InlineData("c:\\example")]
    [InlineData("example:stream")]
    public void NoncanonicalDestinationsAreRejected(string name)
    {
        var parse = () => OwnedAgentProfileDocument.Parse(name, Source);
        parse.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("kora-agent-format: 1", "kora-agent-format: 2")]
    [InlineData("kora-agent-format: 1", "unsupported-format: 1")]
    [InlineData("name: example", "name: other")]
    [InlineData("runtime: local", "runtime: hosted")]
    [InlineData("references: none", "references: example.instructions.md")]
    [InlineData("tools: none", "tools: application.get_version")]
    [InlineData("maximum-calls: 0", "maximum-calls: 1")]
    [InlineData("maximum-rounds: 4", "maximum-rounds: 0")]
    [InlineData("input: text", "input: audio")]
    [InlineData("output: text", "output: files")]
    [InlineData("description: Original tool-free profile", "unknown: value")]
    public void UnsupportedOrNonToolFreeProposalsAreRejected(string before, string after)
    {
        var parse = () => OwnedAgentProfileDocument.Parse("example", Source.Replace(before, after, StringComparison.Ordinal));
        parse.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void OversizedSourceIsRejected()
    {
        var parse = () => OwnedAgentProfileDocument.Parse("example", Source + new string('x', 65536));
        parse.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void OtherwiseValidToolCapableV1ProfileCannotBeAuthored()
    {
        var text = Source.Replace("tools: none", "tools: application.get_version", StringComparison.Ordinal)
            .Replace("maximum-calls: 0", "maximum-calls: 1", StringComparison.Ordinal);
        var parse = () => OwnedAgentProfileDocument.Parse("example", text);
        parse.Should().Throw<InvalidDataException>();
    }
}
