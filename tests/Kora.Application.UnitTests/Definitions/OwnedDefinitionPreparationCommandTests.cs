using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;

namespace Kora.Application.UnitTests.Definitions;

public sealed class OwnedDefinitionPreparationCommandTests
{
    [Theory]
    [InlineData("prepare owned definition instruction alpha", "Kora", ArtifactKind.Instruction, "alpha")]
    [InlineData("prepare owned definition prompt prompt-1", "Nova", ArtifactKind.Prompt, "prompt-1")]
    [InlineData("Nova, prepare owned definition prompt z", "Nova", ArtifactKind.Prompt, "z")]
    [InlineData("Nova prepare owned definition instruction 1", "Nova", ArtifactKind.Instruction, "1")]
    [InlineData("prepare owned definition instruction a", "prepare", ArtifactKind.Instruction, "a")]
    [InlineData("owned definition, prepare owned definition prompt a", "owned definition", ArtifactKind.Prompt, "a")]
    public void ExactGrammarPreservesOnlyExplicitKindAndCanonicalFilenameId(string text, string name, ArtifactKind kind, string id)
    {
        var command = OwnedDefinitionPreparationCommand.Parse(text, name)!;
        command.IsValid.Should().BeTrue();
        command.IsHelp.Should().BeFalse();
        command.Kind.Should().Be(kind);
        command.OwnedId.Should().Be(id);
        command.ToString().Should().Be(nameof(OwnedDefinitionPreparationCommand)).And.NotContain(id.Length > 1 ? id : "private");
    }

    [Theory]
    [InlineData("owned definition help")]
    [InlineData("Kora, owned definition help")]
    public void ConstantHelpCarriesNoIdentifier(string text)
    {
        var command = OwnedDefinitionPreparationCommand.Parse(text, "Kora")!;
        command.IsHelp.Should().BeTrue();
        command.IsValid.Should().BeTrue();
        command.Kind.Should().BeNull();
        command.OwnedId.Should().BeNull();
        OwnedDefinitionPreparationCommand.Syntax.Should().Contain("AND-composed").And.Contain("reads nothing");
    }

    [Theory]
    [InlineData("prepare owned")]
    [InlineData("prepare owned definition")]
    [InlineData("prepare owned definition Instruction example")]
    [InlineData("prepare owned definition PROMPT example")]
    [InlineData("PREPARE OWNED DEFINITION instruction example")]
    [InlineData("prepare  owned definition instruction example")]
    [InlineData(" prepare owned definition instruction example")]
    [InlineData("owned definition Help")]
    [InlineData("owned definition help tail")]
    [InlineData("prepare owned definition skill example")]
    [InlineData("prepare owned definition instruction")]
    [InlineData("prepare owned definition instruction a b")]
    [InlineData("prepare owned definition instruction a\t")]
    [InlineData("prepare owned definition instruction a\n")]
    [InlineData("Nova, prepare owned definition instruction example")]
    [InlineData("Kora,prepare owned definition instruction example")]
    [InlineData("owned definition help ")]
    public void ReservedMalformedGrammarIsNotModelOrQuestionInput(string text)
    {
        var command = OwnedDefinitionPreparationCommand.Parse(text, "Kora")!;
        command.Should().NotBeNull();
        command.IsValid.Should().BeFalse();
        command.Kind.Should().BeNull();
        command.OwnedId.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" Alpha")]
    [InlineData("alpha ")]
    [InlineData("Alpha")]
    [InlineData("alpha.instructions.md")]
    [InlineData("alpha.prompt.md")]
    [InlineData("owned.alpha")]
    [InlineData("../alpha")]
    [InlineData("..\\alpha")]
    [InlineData("C:\\alpha")]
    [InlineData("alpha/child")]
    [InlineData("alpha:stream")]
    [InlineData("\"alpha\"")]
    [InlineData("con")]
    [InlineData("com1")]
    [InlineData("-alpha")]
    [InlineData("alpha-")]
    [InlineData("é")]
    [InlineData("e\u0301")]
    [InlineData("界")]
    [InlineData("alpha\u202e")]
    [InlineData("alpha\0")]
    public void IdAdmissionReusesAuthoritativeExactOwnedQueryPolicy(string id)
    {
        var command = OwnedDefinitionPreparationCommand.Parse("prepare owned definition instruction " + id, "Kora")!;
        command.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    [InlineData(512, false)]
    public void CompleteBoundsAreNeverTruncated(int size, bool valid) =>
        OwnedDefinitionPreparationCommand.Parse("prepare owned definition prompt " + new string('a', size), "Kora")!
            .IsValid.Should().Be(valid);

    [Theory]
    [InlineData("")]
    [InlineData("prepare")]
    [InlineData("hello world")]
    [InlineData("owned metadata")]
    [InlineData("prepare file search for \"example\"")]
    [InlineData("Kora, instructions editor")]
    [InlineData("Nova, prepare file search")]
    public void UnrelatedFamiliesAreNotClaimed(string text) =>
        OwnedDefinitionPreparationCommand.Parse(text, "Kora").Should().BeNull();
}
