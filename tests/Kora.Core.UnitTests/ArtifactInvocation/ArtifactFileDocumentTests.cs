using System.Text;

using AwesomeAssertions;

using Kora.Core.Artifacts;

namespace Kora.Core.UnitTests.Artifacts;

public sealed class ArtifactFileDocumentTests
{
    [Theory]
    [InlineData(false, "\n")]
    [InlineData(true, "\r\n")]
    public void ParseAndEncodeRoundTripExactOriginalBytesAndMetadata(bool bom, string newline)
    {
        var text = string.Join(newline, "---", "kora-format: 1", "name: 'Example'",
            "description: \"Read only\"", "attribution: My own text", "---", "  Body  ", "", "Last line", "");
        var bytes = ArtifactFileDocument.Encode(text, bom);
        var document = ArtifactFileDocument.Parse(bytes, ArtifactKind.Instruction, "kora-profile", "example");
        document.RequireEditableInstruction();
        document.HasBom.Should().Be(bom);
        document.Text.Should().Be(text);
        document.Metadata["attribution"].Should().Be("My own text");
        document.Definition.OwnedInstructionName.Should().Be("example");
        document.Definition.RequiresInstructionReview.Should().BeTrue();
        document.Definition.OwnedInstructionRevision.Should().BeNull();
        document.CopyBytes().Should().Equal(bytes);
        ArtifactFileDocument.Encode(document.Text, document.HasBom).Should().Equal(bytes);
        document.CopyBytes()[0] = 0;
        document.CopyBytes().Should().Equal(bytes);
        new OwnedInstructionSnapshot("example", "aaa", "bbb", document).Revision.Should()
            .StartWith(document.Definition.Digest + ".aaa.bbb.").And.HaveLength(137);
    }

    [Theory]
    [InlineData("")]
    [InlineData("body")]
    [InlineData("---\ninvalid\n---\nbody")]
    [InlineData("---\nname: example\nname: duplicate\n---\nbody")]
    [InlineData("---\ndescription: test\nbody")]
    [InlineData("---\ndescription: test")]
    [InlineData("---\ndescription: test\n---\n")]
    [InlineData("---\nname: 😀\ndescription: test\n---\nbody")]
    [InlineData("---\nname: invalid\0name\ndescription: test\n---\nbody")]
    [InlineData("---\nname: example\n---\nbody")]
    public void ParseRejectsInvalidFilesWithoutManufacturingAUsableDefinition(string text)
    {
        var parse = () => ArtifactFileDocument.Parse(Encoding.UTF8.GetBytes(text), ArtifactKind.Instruction, "kora-profile", "example");
        parse.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void StrictUnicodeOriginalByteCapacityAndLegacyReaderBehaviorArePreserved()
    {
        ((Action)(() => ArtifactFileDocument.Parse([0xff], ArtifactKind.Instruction, "kora-profile", "example")))
            .Should().Throw<InvalidDataException>();
        ((Action)(() => ArtifactFileDocument.Encode("\ud800", false))).Should().Throw<InvalidDataException>();
        ((Action)(() => ArtifactFileDocument.Encode(null!, true))).Should().Throw<ArgumentNullException>();
        var header = "---\ndescription: example\n---\n";
        var maximum = Encoding.UTF8.GetBytes(header + new string('a', ArtifactDefinition.MaximumContentLength - header.Length));
        ArtifactFileDocument.Parse(maximum, ArtifactKind.Instruction, "kora-profile", "example").CopyBytes().Should().HaveCount(65536);
        ((Action)(() => ArtifactFileDocument.Parse([.. maximum, 0x61], ArtifactKind.Instruction, "kora-profile", "example")))
            .Should().Throw<InvalidDataException>();
        var prompt = ArtifactFileDocument.Parse(Encoding.UTF8.GetBytes("---\nname: Prompt\n---\nbody"),
            ArtifactKind.Prompt, "additional", "prompt");
        prompt.Definition.Description.Should().Be("Run the Prompt prompt.");
        prompt.Definition.OwnedInstructionName.Should().BeNull();
        ((Action)(() => prompt.RequireEditableInstruction())).Should().Throw<InvalidDataException>();
        var legacy = ArtifactFileDocument.Parse(Encoding.UTF8.GetBytes("---\ndescription: legacy\n---\nbody"),
            ArtifactKind.Instruction, "kora-profile", "legacy");
        legacy.RequireEditableInstruction();
        legacy.Definition.RequiresInstructionReview.Should().BeFalse();
        ((Action)(() => ArtifactFileDocument.Parse(Encoding.UTF8.GetBytes("---\nname: other\ndescription: skill\n---\nbody"),
            ArtifactKind.Skill, "additional", "folder"))).Should().Throw<InvalidDataException>();
        var skill = ArtifactFileDocument.Parse(Encoding.UTF8.GetBytes("---\nname: folder\ndescription: skill\n---\nbody"),
            ArtifactKind.Skill, "additional", "folder");
        skill.Definition.CommandName.Should().Be("folder");
    }

    [Theory]
    [InlineData("applyTo: '**'")]
    [InlineData("kora-format: 2")]
    [InlineData("description: path\\escape")]
    [InlineData("description: bad\tvalue")]
    public void UnsupportedOriginalsRemainReadableButCannotBeImplicitlyRepairedInPlace(string metadata)
    {
        var text = metadata.StartsWith("description:", StringComparison.Ordinal)
            ? $"---\n{metadata}\n---\nbody" : $"---\ndescription: test\n{metadata}\n---\nbody";
        var document = ArtifactFileDocument.Parse(Encoding.UTF8.GetBytes(text), ArtifactKind.Instruction, "kora-profile", "example");
        ((Action)(() => document.RequireEditableInstruction())).Should().Throw<InvalidDataException>();
        document.CopyBytes().Should().Equal(Encoding.UTF8.GetBytes(text));
    }

    [Fact]
    public void HeaderLimitsNameBoundsAndSourceScopeAreEnforced()
    {
        var header = "---\ndescription: test\n" + string.Join("\n", Enumerable.Range(0, 17).Select(i => $"key{i}: value")) + "\n---\nbody";
        var large = ArtifactFileDocument.Parse(Encoding.UTF8.GetBytes(header), ArtifactKind.Instruction, "kora-profile", "example");
        ((Action)(() => large.RequireEditableInstruction())).Should().Throw<InvalidDataException>();
        var bytes = Encoding.UTF8.GetBytes("---\ndescription: test\nattribution: " + new string('x', 4096) + "\n---\nbody");
        ((Action)(() => ArtifactFileDocument.Parse(bytes, ArtifactKind.Instruction, "kora-profile", "example").RequireEditableInstruction()))
            .Should().Throw<InvalidDataException>();
        ((Action)(() => ArtifactFileDocument.Parse(Encoding.UTF8.GetBytes("---\nname: " + new string('a', 65) + "\ndescription: test\n---\nbody"),
            ArtifactKind.Instruction, "kora-profile", "example"))).Should().Throw<InvalidDataException>();
        var shared = ArtifactFileDocument.Parse(Encoding.UTF8.GetBytes("---\ndescription: test\n---\nbody"), ArtifactKind.Instruction, "additional", "example");
        ((Action)(() => shared.RequireEditableInstruction())).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("..0-example..more")]
    [InlineData("Name 9")]
    public void ReaderRetainsDeclaredNamesWhileProducingItsAuthoritativeCommand(string name)
    {
        var document = ArtifactFileDocument.Parse(Encoding.UTF8.GetBytes($"---\nname: {name}\ndescription: test\n---\nbody"),
            ArtifactKind.Instruction, "kora-profile", "example");
        document.Definition.Name.Should().Be(name);
        document.Definition.CommandName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void WithheldCatalogueCandidatesAreNotAvailableToTypedOrActivatedLookup()
    {
        var definition = ArtifactFileDocument.Parse(Encoding.UTF8.GetBytes("---\ndescription: test\n---\nbody"),
            ArtifactKind.Instruction, "kora-profile", "example").Definition;
        var catalogue = new ArtifactCatalogue([definition with { IsUseEnabled = false }]);
        catalogue.Candidates.Should().ContainSingle();
        catalogue.Artifacts.Should().BeEmpty();
        catalogue.IsCurrent(definition.Id, definition.Digest).Should().BeFalse();
        new ArtifactCommandRouter(catalogue).Match("/instruction example", "Kora").Invocation.Should().BeNull();
        new ArtifactCommandRouter(catalogue).Match("Kora, use example", "Kora").IsArtifactCommand.Should().BeFalse();
        var changes = 0;
        catalogue.Changed += (_, _) => changes++;
        catalogue.Publish([definition]);
        catalogue.IsCurrent(definition.Id, definition.Digest).Should().BeTrue();
        changes.Should().Be(1);
    }

    [Fact]
    public void SharedDiscoveryReaderPreservesHiddenSkillFilteringBeforeInvocationMetadataValidation()
    {
        ArtifactFileDocument.ParseInvocable(Encoding.UTF8.GetBytes("---\nuser-invocable: false\n---\nBody"),
            ArtifactKind.Skill, "additional", "hidden").Should().BeNull();
        ArtifactFileDocument.ParseInvocable(Encoding.UTF8.GetBytes("---\ndescription: test\n---\nBody"),
            ArtifactKind.Skill, "additional", "skill")!.Definition.CommandName.Should().Be("skill");
        ArtifactFileDocument.ParseInvocable(Encoding.UTF8.GetBytes("---\ndescription: test\n---\nBody"),
            ArtifactKind.Instruction, "additional", "instruction")!.Definition.Kind.Should().Be(ArtifactKind.Instruction);
    }

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", true)]
    [InlineData("\r", false)]
    public void NativeVersionProposalRetainsLegacySourceAndIsIdempotent(string newline, bool bom)
    {
        var original = string.Join(newline, "---", "description: Test", "attribution: Own text", "---", "Body", "");
        var document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(original, bom), ArtifactKind.Instruction, "kora-profile", "example");
        var proposed = document.WithNativeInstructionVersion("example");
        proposed.Text.Should().Be("---" + newline + "kora-format: 1" + newline + original[(3 + newline.Length)..]);
        proposed.HasBom.Should().Be(bom);
        proposed.WithNativeInstructionVersion("example").Should().BeSameAs(proposed);
        proposed.Metadata["attribution"].Should().Be("Own text");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnconfirmedPublicationExceptionRetainsObservedCancellationWithoutClaimingRollback(bool cancelled)
    {
        var cause = new IOException("Synthetic storage failure");
        var failure = new InstructionPublicationUnconfirmedException(cancelled, cause);
        failure.CancellationRequested.Should().Be(cancelled);
        failure.InnerException.Should().BeSameAs(cause);
        failure.Message.Should().Contain("publication occurred").And.Contain("No rollback");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void SharedExactActivationPrefixRejectsMissingInput(string? text)
    {
        ((Action)(() => ArtifactCommandRouter.RemoveActivationPrefix(text!, "Kora"))).Should().Throw<ArgumentException>();
    }
}
