using AwesomeAssertions;
using Kora.Core.Customizations;
using Kora.Core.Dependencies;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class SelectedHostedDefinitionTests
{
    [Theory]
    [InlineData(CustomizationKind.Skill, 16384)]
    [InlineData(CustomizationKind.Agent, 16384)]
    [InlineData(CustomizationKind.Skill, 16385)]
    public void ExactSourceMustFitInclusiveUtf8BoundWithoutTruncation(CustomizationKind kind, int characters)
    {
        var text = new string('\u00e9', characters);
        if (characters == 16385)
        {
            Action create = () => _ = new SelectedHostedDefinition(kind, "source", "revision", "reviewed", "Description", text);
            create.Should().Throw<InvalidDataException>();
            return;
        }
        var copy = new SelectedHostedDefinition(kind, "source", "revision", "reviewed", "Description", text);
        copy.SourceText.Should().Be(text);
        System.Text.Encoding.UTF8.GetByteCount(copy.SourceText).Should().Be(32768);
        copy.Kind.Should().Be(kind);
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("bom")]
    [InlineData("surrogate")]
    [InlineData("name")]
    [InlineData("description")]
    [InlineData("source-id")]
    [InlineData("revision")]
    [InlineData("name-length")]
    [InlineData("name-character")]
    [InlineData("description-length")]
    public void UnsupportedOrMalformedCopyIsRejected(string fault)
    {
        Action create = () => _ = new SelectedHostedDefinition(
            fault is "kind" ? CustomizationKind.Prompt : CustomizationKind.Skill,
            fault is "source-id" ? new('s', 1025) : "source",
            fault is "revision" ? new('r', 257) : "revision",
            fault is "name" ? "../source" : fault is "name-length" ? new('n', 65)
                : fault is "name-character" ? "reviewed/path" : "reviewed",
            fault is "description" ? "Hidden\ninstruction" : fault is "description-length" ? new('d', 1025) : "Description",
            fault is "bom" ? "\ufeffsource" : fault is "surrogate" ? "\ud800" : "Exact source");
        create.Should().Throw<InvalidDataException>();
    }
}
