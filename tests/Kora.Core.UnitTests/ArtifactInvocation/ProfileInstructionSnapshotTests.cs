using AwesomeAssertions;

using Kora.Core.Artifacts;
using Kora.Core.Context;

namespace Kora.Core.UnitTests.ArtifactInvocation;

public sealed class ProfileInstructionSnapshotTests
{
    private const string Path = @"C:\Users\example\AppData\Roaming\Code\User\prompts\example.instructions.md";
    private static readonly Guid Source = new("fb2c8d44-4aa5-422e-ab97-d94396e4bd3f");

    private static ProfileInstructionSnapshot Parse(string text, bool bom = false)
    {
        var bytes = ArtifactFileDocument.Encode(text, bom);
        return ProfileInstructionSnapshot.Parse(Source, new(Path, "native-file", bytes.Length, DateTimeOffset.UnixEpoch), bytes);
    }

    [Theory]
    [InlineData("\r\n", true, "attribution: 'Original author'")]
    [InlineData("\n", false, "attribution: Original author")]
    [InlineData("\r", true, "attribution:")]
    [InlineData("\n", true, "")]
    [InlineData("\r\n", false, "")]
    [InlineData("\r", false, "")]
    [InlineData("\n", false, "  attribution:   'Original author'   ")]
    [InlineData("\n", false, "attribution: \"Original author\"")]
    [InlineData("\n", false, "attribution: '")]
    [InlineData("\n", false, "attribution: 'x\"")]
    [InlineData("\n", false, "attribution: [value]")]
    [InlineData("\n", false, "attribution: aa")]
    [InlineData("\n", false, "attribution:     ")]
    public void ForkPreservesAllOriginalBytesExceptReviewedNameAndExplicitAttribution(string newline, bool bom, string attribution)
    {
        var text = $"---{newline}kora-format: 1{newline}name: 'example'{newline}description: Test{newline}"
            + (attribution.Length > 0 ? attribution + newline : "")
            + $"---{newline}Body with unicode \u00e9 and trailing spaces.  {newline}";
        var source = Parse(text, bom);
        var original = source.Document.CopyBytes();
        var copy = source.Document.WithProfileInstructionCopyName("separate", source.Identity);
        source.Document.CopyBytes().Should().Equal(original);
        copy.HasBom.Should().Be(bom);
        copy.Definition.Name.Should().Be("separate");
        copy.Definition.Source.Should().Be("kora-profile");
        copy.Definition.OwnedInstructionName.Should().Be("separate");
        copy.Definition.RequiresInstructionReview.Should().BeTrue();
        copy.Text.Should().Contain($"name: 'separate'{newline}description: Test{newline}");
        copy.Text.Should().EndWith($"---{newline}Body with unicode \u00e9 and trailing spaces.  {newline}");
        copy.Metadata["attribution"].Should().StartWith("Profile instruction fork " + source.Identity);
        if (source.Document.Metadata.TryGetValue("attribution", out var author))
        { copy.Metadata["attribution"].Should().EndWith("; original attribution:" + (author.Length > 0 ? " " + author : string.Empty)); }
        copy.RequireOwnedCopySource();
        ((Action)source.Document.RequireEditableInstruction).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("kora-format: 2\n")]
    [InlineData("kora-format: 99\n")]
    [InlineData("kora-format: 1\napplyTo: '**'\n")]
    [InlineData("kora-format: 1\ntools: shell\n")]
    [InlineData("kora-format: 1\ndependencies: other\n")]
    [InlineData("kora-format: 1\nkora-format: 1\n")]
    [InlineData("kora-format: 1\nattribution: bad\\path\n")]
    public void UnsupportedNativeMetadataIsRejectedWithoutFlatteningOrRepair(string metadata)
    {
        var parse = () => Parse($"---\n{metadata}name: example\ndescription: Test\n---\nGuidance");
        parse.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("SKILL.md", false)]
    [InlineData("example.prompt.md", false)]
    [InlineData("example.Instructions.md", false)]
    [InlineData("example.instructions.md", true)]
    public void SnapshotRequiresExactCanonicalInstructionFilenameAndCompleteLeaseLength(string filename, bool wrongLength)
    {
        var bytes = ArtifactFileDocument.Encode("---\nkora-format: 1\nname: example\ndescription: Test\n---\nBody", false);
        var parse = () => ProfileInstructionSnapshot.Parse(Source,
            new(@"C:\Users\example\" + filename, "identity", bytes.Length + (wrongLength ? 1 : 0), DateTimeOffset.UnixEpoch), bytes);
        parse.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void IdentityIncludesLeaseFileReaderAndExactBytesAndNewNameMustBeDistinct()
    {
        const string text = "---\nkora-format: 1\nname: example\ndescription: Test\n---\nBody";
        var source = Parse(text);
        var bytes = ArtifactFileDocument.Encode(text, false);
        var changedLease = ProfileInstructionSnapshot.Parse(Guid.NewGuid(), source.Metadata, bytes);
        var changedFile = ProfileInstructionSnapshot.Parse(Source, source.Metadata with { FileIdentity = "replacement" }, bytes);
        changedLease.Identity.Should().NotBe(source.Identity);
        changedFile.Identity.Should().NotBe(source.Identity);
        Parse(text + "!").Identity.Should().NotBe(source.Identity);
        ((Action)(() => source.Document.WithProfileInstructionCopyName("example", source.Identity))).Should().Throw<InvalidDataException>();
        ((Action)(() => source.Document.WithProfileInstructionCopyName("separate", "invalid"))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MalformedUtf8AndWholeSourceOrAttributionGrowthOverBoundsAreRejected()
    {
        var malformed = () => ProfileInstructionSnapshot.Parse(Source, new(Path, "file", 1, DateTimeOffset.UnixEpoch), [0xff]);
        malformed.Should().Throw<InvalidDataException>();
        var text = "---\nkora-format: 1\nname: example\ndescription: Test\n---\n";
        var maximum = Parse(text + new string('a', ArtifactDefinition.MaximumContentLength - text.Length));
        ((Action)(() => maximum.Document.WithProfileInstructionCopyName("separate", maximum.Identity))).Should().Throw<InvalidDataException>();
        ((Action)(() => Parse(text + new string('a', ArtifactDefinition.MaximumContentLength)))).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(ArtifactKind.Prompt, "profile-instruction")]
    [InlineData(ArtifactKind.Skill, "profile-instruction")]
    [InlineData(ArtifactKind.Instruction, "kora-profile")]
    [InlineData(ArtifactKind.Instruction, "vscode-profile")]
    public void ProfileSourceValidationRejectsWrongKindOrReaderIdentity(ArtifactKind kind, string source)
    {
        var bytes = ArtifactFileDocument.Encode("---\nkora-format: 1\nname: example\ndescription: Test\n---\nBody", false);
        var document = ArtifactFileDocument.Parse(bytes, kind, source, "example");
        ((Action)document.RequireProfileInstructionSource).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(true, "native-file")]
    [InlineData(false, "")]
    [InlineData(false, " ")]
    public void SnapshotRejectsMissingLeaseOrFileIdentity(bool emptyLease, string identity)
    {
        var bytes = ArtifactFileDocument.Encode("---\nkora-format: 1\nname: example\ndescription: Test\n---\nBody", false);
        var parse = () => ProfileInstructionSnapshot.Parse(emptyLease ? Guid.Empty : Source,
            new(Path, identity, bytes.Length, DateTimeOffset.UnixEpoch), bytes);
        parse.Should().Throw<InvalidDataException>();
    }
}