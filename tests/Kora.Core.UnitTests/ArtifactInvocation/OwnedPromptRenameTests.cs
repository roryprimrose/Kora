using AwesomeAssertions;

using Kora.Core.Artifacts;

namespace Kora.Core.UnitTests.ArtifactInvocation;

public sealed class OwnedPromptRenameTests
{
    [Theory]
    [InlineData("1", false, "\n", "name :  'original'  \n")]
    [InlineData("2", true, "\r\n", "name: \"original\"\r\n")]
    [InlineData("2", false, "\r", "name: original\r")]
    [InlineData("1", true, "\n", "name: original\n")]
    public void OnlyDeclaredNameBytesAndFilenameIdentityChange(string version, bool bom, string newline, string nameLine)
    {
        var text = "---" + newline + "kora-format: " + version + newline + nameLine
            + "description: Exact request" + newline + "attribution: Original author." + newline + "---" + newline
            + (version is "2" ? "Literal ${input:second} then ${input:first}, ${input:second}." : "Literal saved request.")
            + "\r\n  Preserve  spacing.\nNo final newline";
        var source = Source(text, bom);
        var proposal = new OwnedPromptRename(source, "renamed");

        proposal.Source.Should().BeSameAs(source);
        proposal.DestinationName.Should().Be("renamed");
        proposal.Proposed.CopyBytes().Should().Equal(ArtifactFileDocument.Encode(
            nameLine.Length == 0 ? text : text.Replace(nameLine, nameLine.Replace("original", "renamed", StringComparison.Ordinal), StringComparison.Ordinal), bom));
        proposal.Proposed.HasBom.Should().Be(bom);
        proposal.Proposed.Body.Should().Be(source.Document.Body);
        proposal.Proposed.Metadata["attribution"].Should().Be(source.Document.Metadata["attribution"]);
        proposal.Proposed.Definition.Id.Should().NotBe(source.Document.Definition.Id);
        source.CanonicalFileName.Should().Be("original.prompt.md");
        source.Document.CopyBytes().Should().Equal(ArtifactFileDocument.Encode(text, bom));
    }

    [Theory]
    [InlineData("original")]
    [InlineData("ORIGINAL")]
    [InlineData("CON")]
    [InlineData("help")]
    [InlineData("prompts")]
    [InlineData("instructions")]
    [InlineData("../other")]
    [InlineData("new.prompt.md")]
    [InlineData("")]
    public void InvalidReservedAndCaseOnlyDestinationsNeverBecomeProposals(string destination)
    {
        var source = Source();
        ((Action)(() => _ = new OwnedPromptRename(source, destination))).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void NonCanonicalLegacyForeignKindAndUnchangedIdentityAreRejected()
    {
        ((Action)(() => _ = new OwnedPromptRename(null!, "renamed"))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new OwnedPromptRename(Source() with { CanonicalFileName = "Original.prompt.md" }, "renamed"))).Should().Throw<InvalidDataException>();
        ((Action)(() => _ = new OwnedPromptRename(Source() with { Name = "Legacy.Name" }, "renamed"))).Should().Throw<InvalidDataException>();
        var instruction = Source() with
        {
            Document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                "---\nkora-format: 1\nname: original\ndescription: Test\n---\nBody", false), ArtifactKind.Instruction, "kora-profile", "original"),
        };
        ((Action)(() => _ = new OwnedPromptRename(instruction, "renamed"))).Should().Throw<InvalidDataException>();
        ((Action)(() => _ = new OwnedInstructionRename(Source(), "renamed"))).Should().Throw<InvalidDataException>();
        ((Action)(() => _ = new OwnedPromptRename(Source("---\nname: original\ndescription: Test\n---\nBody"), "renamed"))).Should().Throw<InvalidDataException>();
        ((Action)(() => _ = new OwnedPromptRename(Source("---\nkora-format: 2\nname: renamed\ndescription: Test\n---\nBody"), "renamed"))).Should().Throw<InvalidDataException>();
    }

    private static OwnedInstructionSnapshot Source(string text = "---\nkora-format: 2\nname: original\ndescription: Test\n---\nLiteral ${input:topic}", bool bom = false) =>
        new("original", "directory", "file",
            ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(text, bom), ArtifactKind.Prompt, "kora-profile", "original"));
}
