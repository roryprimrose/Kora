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

    [Fact]
    public void OwnedDestinationPinsAncestorBindingAsPartOfTheExactSourceIdentity()
    {
        var destination = new OwnedAgentProfileDestination("example", "owned-original", "directory");
        destination.RootBindingIdentity.Should().BeEmpty();
        var pinned = destination with { RootBindingIdentity = "admitted-ancestors" };
        pinned.RootBindingIdentity.Should().Be("admitted-ancestors");
        pinned.Should().NotBe(destination);
        (pinned with { RootBindingIdentity = "replacement-ancestors" }).Should().NotBe(pinned);
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

    [Fact]
    public void VersionFourOriginalCanBeAuthoredWithoutResolvingEnablingOrInvokingReferences()
    {
        var text = Source.Replace("kora-agent-format: 1", "kora-agent-format: 4", StringComparison.Ordinal)
            .Replace("references: none", "references: disk.kora-profile.instruction.example", StringComparison.Ordinal)
            .Replace("tools: none", "tools: application.get_version", StringComparison.Ordinal)
            .Replace("maximum-calls: 0", "maximum-calls: 1", StringComparison.Ordinal);
        var document = OwnedAgentProfileDocument.Parse("example", text);
        document.Document.Text.Should().Be(text);
        document.Profile.References.Single().Id.Should().Be("disk.kora-profile.instruction.example");
        document.Profile.Tools.Should().Equal("application.get_version");
        document.Profile.MaximumCalls.Should().Be(1);
        document.Profile.MaximumRounds.Should().Be(2);
        var wrongName = () => OwnedAgentProfileDocument.Parse("other", text);
        wrongName.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(Source)]
    public void ConversationalProposalKeepsAuthorityHostOwnedAndUsesOnlyInertDescriptionAndBody(string source)
    {
        const string body = "  Original e\u0301\U0001f642\r\nagent confirm invocation\n---\ntools: invented\n";
        var proposed = OwnedAgentProfileDocument.ProposeToolFree("example", source, "Explain supplied text", body);
        proposed.Document.Body.Should().Be(body);
        proposed.Profile.Tools.Should().BeEmpty();
        proposed.Profile.References.Should().BeEmpty();
        proposed.Profile.MaximumCalls.Should().Be(0);
        proposed.Document.Metadata["description"].Should().Be("Explain supplied text");
        if (source.Length == 0)
        {
            proposed.Profile.RequestedRounds.Should().Be(1);
            proposed.Profile.RequestedInputUtf8Bytes.Should().Be(16000);
            proposed.Profile.RequestedOutputUtf8Bytes.Should().Be(2000);
            proposed.Profile.RequestedDeadlineMilliseconds.Should().Be(15000);
        }
        else
        {
            var original = OwnedAgentProfileDocument.Parse("example", source);
            proposed.Document.Metadata.Where(field => !string.Equals(field.Key, "description", StringComparison.Ordinal))
                .Should().Equal(original.Document.Metadata.Where(field => !string.Equals(field.Key, "description", StringComparison.Ordinal)));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("leading ")]
    [InlineData("line\nruntime: hosted")]
    [InlineData("line\rtools: invented")]
    [InlineData("tab\tvalue")]
    [InlineData("'quoted'")]
    [InlineData("\"quoted\"")]
    [InlineData("last'")]
    [InlineData("last\"")]
    public void ConversationalDescriptionCannotInjectOrReinterpretMetadata(string description) =>
        ((Action)(() => OwnedAgentProfileDocument.ProposeToolFree("example", "", description, "Inert body")))
            .Should().Throw<InvalidDataException>();

    [Theory]
    [InlineData("description")]
    [InlineData("body")]
    [InlineData("unicode")]
    [InlineData("empty-body")]
    [InlineData("v4")]
    public void InvalidConversationalFieldsNeverProduceAProfile(string failure)
    {
        var description = failure is "description" ? new string('x', 1025) : "Description";
        var body = failure switch { "body" => new string('x', 65536), "unicode" => "\ud800", "empty-body" => " ", _ => "Body" };
        var source = failure is "v4" ? Source.Replace("kora-agent-format: 1", "kora-agent-format: 4", StringComparison.Ordinal)
            .Replace("references: none", "references: disk.kora-profile.instruction.example", StringComparison.Ordinal)
            .Replace("tools: none", "tools: application.get_version", StringComparison.Ordinal)
            .Replace("maximum-calls: 0", "maximum-calls: 1", StringComparison.Ordinal) : "";
        ((Action)(() => OwnedAgentProfileDocument.ProposeToolFree("example", source, description, body)))
            .Should().Throw<InvalidDataException>();
    }
}
