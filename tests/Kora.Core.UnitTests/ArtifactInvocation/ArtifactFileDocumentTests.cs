using System.Text;

using AwesomeAssertions;

using Kora.Core.Artifacts;

namespace Kora.Core.UnitTests.Artifacts;

public sealed class ArtifactFileDocumentTests
{
    [Theory]
    [InlineData(false, "\n")]
    [InlineData(true, "\r\n")]
    public void PromptBodyReplacementRetainsHeaderBomAndOriginalTemplate(bool bom, string newline)
    {
        var header = string.Join(newline, "---", "kora-format: 2", "name: example", "description: Request", "---", "");
        var original = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(header + "Old ${input:topic}", bom),
            ArtifactKind.Prompt, "kora-profile", "example");
        var proposed = original.WithPromptBody("New ${input:topic}");
        proposed.CopyBytes().Should().Equal(ArtifactFileDocument.Encode(header + "New ${input:topic}", bom));
        original.Body.Should().Be("Old ${input:topic}");
        var instruction = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(header.Replace("kora-format: 2", "kora-format: 1", StringComparison.Ordinal)
            + "Body", false), ArtifactKind.Instruction, "kora-profile", "example");
        ((Action)(() => instruction.WithPromptBody("Request"))).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", true)]
    [InlineData("\r", true)]
    public void BodyReplacementPreservesExactHostHeaderBomAndUntrimmedUnicode(string newline, bool bom)
    {
        var header = string.Join(newline, "---", "kora-format: 1", "name: 'example'  ",
            "description: \"Read only\"", "attribution: Exact host attribution", "---", "");
        const string body = "  e\u0301\U0001f642\r\n---\nname: untrusted-body\n  ";
        var original = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(header + "  old\r\n", bom),
            ArtifactKind.Instruction, "kora-profile", "example");
        original.Body.Should().Be("  old\r\n");
        var replacement = original.WithInstructionBody(body);
        replacement.Body.Should().Be(body);
        replacement.CopyBytes().Should().Equal(ArtifactFileDocument.Encode(header + body, bom));
        replacement.Metadata.Should().BeEquivalentTo(original.Metadata);
        original.Body.Should().Be("  old\r\n");
        ((Action)(() => original.WithInstructionBody("\ud800"))).Should().Throw<InvalidDataException>();
        ((Action)(() => original.WithInstructionBody(new string('x', ArtifactDefinition.MaximumContentLength)))).Should().Throw<InvalidDataException>();
        ((Action)(() => original.WithInstructionBody(" \r\n"))).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void BodyReplacementCannotPromoteAnotherKindOrProfileSourceIntoOwnedInstructionAuthority()
    {
        const string text = "---\nname: example\ndescription: Test\n---\nBody";
        foreach (var (kind, source) in new[] { (ArtifactKind.Prompt, "kora-profile"), (ArtifactKind.Instruction, "user-profile") })
        {
            var original = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(text, false), kind, source, "example");
            ((Action)(() => original.WithInstructionBody("Replacement"))).Should().Throw<InvalidDataException>();
        }
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction, "\n", false, "name: example")]
    [InlineData(ArtifactKind.Prompt, "\r\n", true, "name: 'example'  ")]
    [InlineData(ArtifactKind.Instruction, "\r", true, "name : \"example\"")]
    [InlineData(ArtifactKind.Prompt, "\n", false, "name:example")]
    [InlineData(ArtifactKind.Instruction, "\r\n", false, "name:  example  ")]
    [InlineData(ArtifactKind.Instruction, "\n", false, "name: x")]
    [InlineData(ArtifactKind.Instruction, "\n", false, "name: 'example")]
    public void OwnedCopyChangesOnlyDeclaredNameAndPreservesEveryOtherByte(ArtifactKind kind, string newline, bool bom, string name)
    {
        var text = string.Join(newline, "---", "kora-format: 1", name, "description: Description",
            "attribution: 'Exact own attribution'", "---", "  Body  ", "", "name: example", "");
        var original = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(text, bom), kind, "kora-profile", "example");
        var copy = original.WithOwnedCopyName("separate");
        var position = text.IndexOf(name, StringComparison.Ordinal);
        var replacementName = name.Contains("'example", StringComparison.Ordinal) && !name.EndsWith("'  ", StringComparison.Ordinal)
            && !name.EndsWith('\'') ? "name: separate" : name.Replace(name.EndsWith('x') ? "x" : "example", "separate", StringComparison.Ordinal);
        var expected = text[..position] + replacementName + text[(position + name.Length)..];
        copy.CopyBytes().Should().Equal(ArtifactFileDocument.Encode(expected, bom));
        copy.Metadata["attribution"].Should().Be(original.Metadata["attribution"]);
        copy.Definition.Id.Should().NotBe(original.Definition.Id);
        copy.Definition.OwnedInstructionName.Should().Be("separate");
        copy.Definition.RequiresInstructionReview.Should().BeTrue();
        original.CopyBytes().Should().Equal(ArtifactFileDocument.Encode(text, bom));
    }

    [Fact]
    public void NativeInstructionWithoutDeclaredNameCopiesByDistinctCanonicalFilenameWithoutMetadataRewrite()
    {
        const string text = "---\nkora-format: 1\ndescription: Description\n---\nname: body-is-not-metadata";
        var original = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(text, false), ArtifactKind.Instruction, "kora-profile", "example");
        var copy = original.WithOwnedCopyName("separate");
        copy.CopyBytes().Should().Equal(original.CopyBytes());
        copy.Definition.Id.Should().NotBe(original.Definition.Id);
        copy.Definition.CommandName.Should().Be("separate");
    }

    [Fact]
    public void CopyDoesNotUpgradeLegacyMetadataAndEnforcesByteLimitIncludingBom()
    {
        var legacy = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode("---\nname: example\ndescription: Test\n---\nBody", false),
            ArtifactKind.Instruction, "kora-profile", "example");
        ((Action)(() => legacy.WithOwnedCopyName("separate"))).Should().Throw<InvalidDataException>();
        ((Action)(() => legacy.WithOwnedCopyName("../other"))).Should().Throw<InvalidDataException>();
        var header = "---\r\nkora-format: 1\r\nname: example\r\ndescription: Test\r\n---\r\n";
        var text = header + new string('a', ArtifactDefinition.MaximumContentLength - Encoding.UTF8.GetByteCount(header) - 3);
        var maximum = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(text, true), ArtifactKind.Instruction, "kora-profile", "example");
        maximum.WithOwnedCopyName("shorter").CopyBytes().Should().HaveCount(ArtifactDefinition.MaximumContentLength);
        ((Action)(() => maximum.WithOwnedCopyName("longer-name"))).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(false, "\n", ArtifactKind.Instruction)]
    [InlineData(true, "\r\n", ArtifactKind.Instruction)]
    [InlineData(false, "\n", ArtifactKind.Prompt)]
    [InlineData(true, "\r\n", ArtifactKind.Prompt)]
    public void ParseAndEncodeRoundTripExactOriginalBytesAndMetadata(bool bom, string newline, ArtifactKind kind)
    {
        var text = string.Join(newline, "---", "kora-format: 1", "name: 'Example'",
            "description: \"Read only\"", "attribution: My own text", "---", "  Body  ", "", "Last line", "");
        var bytes = ArtifactFileDocument.Encode(text, bom);
        var document = ArtifactFileDocument.Parse(bytes, kind, "kora-profile", "example");
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
    [InlineData(ArtifactKind.Instruction, "Instructions", ".instructions.md")]
    [InlineData(ArtifactKind.Prompt, "Prompts", ".prompt.md")]
    public void ClosedOwnedKindDefinesOnlyTheHostLayout(ArtifactKind kind, string directory, string extension)
    {
        OwnedDefinitionKind.DirectoryName(kind).Should().Be(directory);
        OwnedDefinitionKind.Extension(kind).Should().Be(extension);
        OwnedDefinitionKind.MaximumFiles(kind).Should().Be(128);
    }

    [Fact]
    public void CatalogueSeparatelyBoundsBothOwnedKindsWithoutEvictingOrWideningOtherArtifacts()
    {
        static ArtifactDefinition Definition(ArtifactKind kind, int index, string source = "kora-profile", bool owned = true)
        {
            var name = kind.ToString().ToLowerInvariant() + "-" + index;
            return new("test." + name, kind, name, "Test", name, [name], source, "1", new string('a', 64), "Body")
            { OwnedInstructionName = owned ? name : null };
        }
        var instructions = Enumerable.Range(0, 128).Select(index => Definition(ArtifactKind.Instruction, index)).ToArray();
        var prompts = Enumerable.Range(0, 128).Select(index => Definition(ArtifactKind.Prompt, index)).ToArray();
        new ArtifactCatalogue([.. instructions, .. prompts]).Candidates.Should().HaveCount(256);
        ((Action)(() => _ = new ArtifactCatalogue([.. instructions, Definition(ArtifactKind.Instruction, 128)]))).Should().Throw<ArgumentException>();
        ((Action)(() => _ = new ArtifactCatalogue([.. prompts, Definition(ArtifactKind.Prompt, 128)]))).Should().Throw<ArgumentException>();
        ((Action)(() => _ = new ArtifactCatalogue(Enumerable.Range(0, 129).Select(index => Definition(ArtifactKind.Prompt, index, "additional")).ToArray())))
            .Should().Throw<ArgumentException>();
        ((Action)(() => _ = new ArtifactCatalogue(Enumerable.Range(0, 129).Select(index => Definition(ArtifactKind.Prompt, index, owned: false)).ToArray())))
            .Should().Throw<ArgumentException>();
    }
    [Theory]
    [InlineData(ArtifactKind.Skill)]
    [InlineData(ArtifactKind.Agent)]
    [InlineData((ArtifactKind)999)]
    public void OtherKindsCannotAcquireOwnedWriterAuthority(ArtifactKind kind)
    {
        ((Action)(() => OwnedDefinitionKind.DirectoryName(kind))).Should().Throw<InvalidDataException>();
        ((Action)(() => OwnedDefinitionKind.Extension(kind))).Should().Throw<InvalidDataException>();
        ((Action)(() => OwnedDefinitionKind.MaximumFiles(kind))).Should().Throw<InvalidDataException>();
        var document = ArtifactFileDocument.Parse(Encoding.UTF8.GetBytes("---\nname: example\ndescription: Test\n---\nBody"),
            kind == (ArtifactKind)999 ? ArtifactKind.Skill : kind, "kora-profile", "example");
        ((Action)(() => document.RequireEditableInstruction())).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("---\nname: example\n---\nBody")]
    [InlineData("---\ndescription: Test\n---\nBody")]
    [InlineData("---\nname: example\ndescription: Test\n---\n${input:value}")]
    [InlineData("---\nname: example\ndescription: Test\nparameters: required\n---\nBody")]
    public void PromptEditorRejectsUnsupportedParametersOrMissingMetadataWithoutRewritingOriginal(string text)
    {
        var bytes = ArtifactFileDocument.Encode(text, true);
        var document = ArtifactFileDocument.Parse(bytes, ArtifactKind.Prompt, "kora-profile", "example");
        ((Action)(() => document.RequireEditableInstruction())).Should().Throw<InvalidDataException>();
        document.CopyBytes().Should().Equal(bytes);
    }

    [Fact]
    public void LiteralPromptUpgradePreservesSourceAndCrossKindIdentityIsDistinct()
    {
        const string text = "---\r\nname: example\r\ndescription: Test\r\n---\r\nLiteral request.\r\n";
        var prompt = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(text, true), ArtifactKind.Prompt, "kora-profile", "example");
        var upgraded = prompt.WithNativeInstructionVersion("example");
        upgraded.Definition.Kind.Should().Be(ArtifactKind.Prompt);
        upgraded.HasBom.Should().BeTrue();
        upgraded.Text.Should().Be(text.Replace("---\r\nname:", "---\r\nkora-format: 1\r\nname:", StringComparison.Ordinal));
        var instruction = ArtifactFileDocument.Parse(prompt.CopyBytes(), ArtifactKind.Instruction, "kora-profile", "example");
        new OwnedInstructionSnapshot("example", "a", "b", prompt).Revision.Should()
            .NotBe(new OwnedInstructionSnapshot("example", "a", "b", instruction).Revision);
        new Kora.Core.Dependencies.LocalModelArtifact("test", ArtifactKind.Prompt, "Test", "kora-profile", "1",
            prompt.Definition.Digest, "Body") { IsLiteralPrompt = true }.IsLiteralPrompt.Should().BeTrue();
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
