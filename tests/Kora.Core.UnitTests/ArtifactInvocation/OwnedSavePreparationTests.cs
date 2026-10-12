using System.Text;

using AwesomeAssertions;

using Kora.Core.Artifacts;

namespace Kora.Core.UnitTests.Artifacts;

public sealed class OwnedSavePreparationTests
{
    [Theory]
    [InlineData(ArtifactKind.Instruction, false, "\n", false, false)]
    [InlineData(ArtifactKind.Instruction, true, "\r\n", false, true)]
    [InlineData(ArtifactKind.Prompt, true, "\r", true, false)]
    [InlineData(ArtifactKind.Prompt, false, "\r\n", true, true)]
    public void PreparationPreservesExactSourceUnlessChangedLegacyRequiresMarker(ArtifactKind kind, bool bom,
        string newline, bool native, bool changed)
    {
        var text = string.Join(newline, "---", native ? "kora-format: 1" : "attribution: Own original text",
            "name: 'example'", "description: Test", "---", "Literal body.", "");
        var document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(text, bom), kind, "kora-profile", "example");
        var original = new OwnedInstructionSnapshot("example", "directory", "file", document);
        var draft = changed ? text + "Exact changed body." : text;
        var proposed = ArtifactFileDocument.PrepareOwnedSave(kind, "example", original, draft);
        var expected = changed && !native ? draft.Insert(3 + newline.Length, "kora-format: 1" + newline) : draft;
        proposed.CopyBytes().Should().Equal(ArtifactFileDocument.Encode(expected, bom));
        proposed.ByteCount.Should().Be(proposed.CopyBytes().Length);
        document.CopyBytes().Should().Equal(ArtifactFileDocument.Encode(text, bom));
        original.Document.Text.Should().Be(text);
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public void NewLegacyDraftIncludesNativeMarkerAndHasNoInventedBom(ArtifactKind kind)
    {
        const string draft = "---\r\nname: example\r\ndescription: Test\r\n---\r\nBody";
        var proposed = ArtifactFileDocument.PrepareOwnedSave(kind, "example", null, draft);
        proposed.CopyBytes().Should().Equal(ArtifactFileDocument.Encode(draft.Insert(5, "kora-format: 1\r\n"), false));
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("name")]
    [InlineData("filename")]
    [InlineData("metadata")]
    [InlineData("source")]
    [InlineData("unsupported-kind")]
    public void PreparationCannotRepairOrRetargetAnUnsupportedOriginal(string reason)
    {
        var document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\nname: example\ndescription: Test\n" + (reason is "metadata" ? "applyTo: all\n" : "") + "---\nBody", false),
            reason is "kind" ? ArtifactKind.Prompt : ArtifactKind.Instruction, reason is "source" ? "profile" : "kora-profile", "example");
        var original = new OwnedInstructionSnapshot(reason is "name" ? "other" : "example", "directory", "file", document);
        if (reason is "filename") { original = original with { CanonicalFileName = "Example.instructions.md" }; }
        var prepare = () => ArtifactFileDocument.PrepareOwnedSave(reason is "unsupported-kind" ? ArtifactKind.Agent : ArtifactKind.Instruction,
            "example", original, "---\nname: example\ndescription: Test\n---\nChanged");
        prepare.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(false, 65536)]
    [InlineData(true, 65536)]
    [InlineData(false, 65537)]
    [InlineData(true, 65537)]
    public void ChangedLegacyCountsNativeMarkerAndBomAtTheExactFileByteBoundary(bool bom, int size)
    {
        const string header = "---\r\nname: example\r\ndescription: Test\r\n---\r\n";
        const string marker = "kora-format: 1\r\n";
        var text = header + new string('a', size - Encoding.UTF8.GetByteCount(header + marker) - (bom ? 3 : 0));
        var original = new OwnedInstructionSnapshot("example", "directory", "file",
            ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(text[..^1] + "b", bom), ArtifactKind.Instruction, "kora-profile", "example"));
        var prepare = () => ArtifactFileDocument.PrepareOwnedSave(ArtifactKind.Instruction, "example", original, text);
        if (size == 65536) { prepare().ByteCount.Should().Be(size); }
        else { prepare.Should().Throw<InvalidDataException>(); }
        original.Document.ByteCount.Should().Be(size - marker.Length);
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(4097)]
    public void ChangedLegacyCountsVersionMarkerAtTheExactHeaderByteBoundary(int size)
    {
        const string fields = "name: example\r\ndescription: Test\r\nattribution: ";
        var draft = "---\r\n" + fields + new string('x', size - Encoding.UTF8.GetByteCount(fields + "\r\nkora-format: 1\r\n"))
            + "\r\n---\r\nBody";
        var original = new OwnedInstructionSnapshot("example", "directory", "file",
            ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(draft + " old", true), ArtifactKind.Instruction, "kora-profile", "example"));
        var prepare = () => ArtifactFileDocument.PrepareOwnedSave(ArtifactKind.Instruction, "example", original, draft);
        if (size == 4096) { prepare().Metadata["kora-format"].Should().Be("1"); }
        else { prepare.Should().Throw<InvalidDataException>(); }
    }
}
