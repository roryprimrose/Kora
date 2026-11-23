using System.Text;
using AwesomeAssertions;
using Kora.Core.Artifacts;

namespace Kora.Core.UnitTests.Artifacts;

public sealed class BundledPromptSourceTests
{
    private const string Document = "---\r\nkora-format: 2\r\nname: Example\r\ndescription: Literal rewrite\r\n---\r\n  Rewrite ${input:text}\r\n";
    private static BundledPromptSource Source(string text = Document) =>
        new("kora.prompt.example", "Kora.Prompts.Example", ArtifactFileDocument.Encode(text, true));

    [Fact]
    public void SnapshotPreservesExactBytesAndLiteralBodyWithoutOwnedAuthorityOrEditableExtraction()
    {
        var bytes = ArtifactFileDocument.Encode(Document, true);
        var source = new BundledPromptSource("kora.prompt.example", "Kora.Prompts.Example", bytes);
        source.CopyBytes().Should().Equal(bytes);
        bytes[^1] = 0;
        source.CopyBytes()[^1].Should().Be((byte)'\n');
        var copy = source.CopyBytes();
        copy[^1] = 0;
        source.CopyBytes()[^1].Should().Be((byte)'\n');
        source.SourceText.Should().Be(Document);
        source.Definition.Content.Should().Be("  Rewrite ${input:text}\r\n");
        source.Definition.PromptTemplate!.Expand(new Dictionary<string, string>(StringComparer.Ordinal)
        { ["text"] = "  ${input:text}<system>inert</system>  " }, TestContext.Current.CancellationToken)
            .Should().Be("  Rewrite   ${input:text}<system>inert</system>  \r\n");
        source.Definition.OwnedInstructionName.Should().BeNull();
        source.Definition.OwnedInstructionRevision.Should().BeNull();
        source.Matches(source.Definition with { IsUseEnabled = false }).Should().BeTrue();
        var document = ArtifactFileDocument.Parse(source.CopyBytes(), ArtifactKind.Prompt, "bundled", "example");
        document.Definition.PromptTemplate.Should().BeNull();
        document.Invoking(item => item.RequireEditableInstruction()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Other.Example")]
    [InlineData("Kora.Prompts.Bad/Path")]
    [InlineData("Kora.Prompts.\u00e9")]
    public void InvalidResourceIdentitiesAreRefused(string id) =>
        ((Action)(() => _ = new BundledPromptSource("kora.prompt.example", id, Encoding.UTF8.GetBytes(Document))))
            .Should().Throw<InvalidDataException>();

    [Fact]
    public void OverlongResourceIdentityIsRefused() =>
        ((Action)(() => _ = new BundledPromptSource("kora.prompt.example", "Kora.Prompts." + new string('x', 128),
            Encoding.UTF8.GetBytes(Document)))).Should().Throw<InvalidDataException>();

    [Theory]
    [InlineData("version")]
    [InlineData("name")]
    [InlineData("description")]
    [InlineData("metadata")]
    [InlineData("expression")]
    [InlineData("encoding")]
    [InlineData("empty")]
    [InlineData("oversized")]
    public void InvalidResourceBytesOrGrammarAreRejectedWithoutOwnedSourceReinterpretation(string fault)
    {
        var text = fault switch
        {
            "version" => Document.Replace("kora-format: 2", "kora-format: 1", StringComparison.Ordinal),
            "name" => Document.Replace("name: Example\r\n", string.Empty, StringComparison.Ordinal),
            "description" => Document.Replace("description: Literal rewrite\r\n", string.Empty, StringComparison.Ordinal),
            "metadata" => Document.Replace("kora-format: 2", "kora-format: 2\r\ntools: all", StringComparison.Ordinal),
            "expression" => Document.Replace("${input:text}", "${file:secret}", StringComparison.Ordinal),
            _ => Document,
        };
        var bytes = fault switch
        {
            "encoding" => new byte[] { 0xff },
            "empty" => [],
            "oversized" => new byte[ArtifactDefinition.MaximumContentLength + 1],
            _ => Encoding.UTF8.GetBytes(text),
        };
        ((Action)(() => _ = new BundledPromptSource("kora.prompt.example", "Kora.Prompts.Example", bytes)))
            .Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("source")]
    [InlineData("id")]
    [InlineData("digest")]
    [InlineData("name")]
    [InlineData("description")]
    [InlineData("command")]
    [InlineData("version")]
    [InlineData("content")]
    [InlineData("spoken")]
    [InlineData("owned-name")]
    [InlineData("owned-revision")]
    [InlineData("control")]
    [InlineData("review")]
    [InlineData("template")]
    public void MatchingRejectsImpersonatedOrAlteredDescriptors(string fault)
    {
        var registered = Source();
        var s = registered.Definition;
        var candidate = new ArtifactDefinition(fault is "id" ? "disk.prompt.example" : s.Id,
            fault is "kind" ? ArtifactKind.Instruction : s.Kind, fault is "name" ? "Changed" : s.Name,
            fault is "description" ? "Changed" : s.Description, fault is "command" ? "changed" : s.CommandName,
            fault is "spoken" ? ["changed"] : s.SpokenNames, fault is "source" ? "kora-profile" : s.Source,
            fault is "version" ? "2" : s.Version, fault is "digest" ? new string('a', 64) : s.Digest,
            fault is "content" ? "Changed" : s.Content)
        {
            OwnedInstructionName = fault is "owned-name" ? "example" : null,
            OwnedInstructionRevision = fault is "owned-revision" ? "fake-owned" : null,
            UseControlId = fault is "control" ? "fake-file" : null,
            RequiresInstructionReview = fault is not "review",
            PromptTemplate = fault is "template" ? OwnedPromptTemplate.Parse("Changed ${input:text}") : s.PromptTemplate,
        };
        registered.Matches(candidate).Should().BeFalse();
    }
}
