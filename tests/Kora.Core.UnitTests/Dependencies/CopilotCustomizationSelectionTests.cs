using AwesomeAssertions;
using Kora.Core.Dependencies;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class CopilotCustomizationSelectionTests
{
    [Theory]
    [InlineData("foreign/name", "Complete source", "source")]
    [InlineData("selected", "Complete source", "absent instructions")]
    public void NonexactNativeIdentityOrInstructionsAreRejected(string name, string source, string instructions)
    {
        Action create = () => _ = new CopilotCustomizationSelection(true, "source-id", "revision", name,
            "Description", source, instructions);
        create.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(16384)]
    [InlineData(16385)]
    public void CompleteUtf8CopyMustFitWithoutTruncation(int characters)
    {
        var source = new string('\u00e9', characters);
        if (characters == 16384)
        {
            var selection = new CopilotCustomizationSelection(false, "source-id", "revision", "selected",
                "Description", source, source);
            selection.SourceText.Should().Be(source);
            System.Text.Encoding.UTF8.GetByteCount(selection.SourceText).Should().Be(32768);
            return;
        }
        Action create = () => _ = new CopilotCustomizationSelection(false, "source-id", "revision", "selected",
            "Description", source, source);
        create.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("source-length")]
    [InlineData("revision-length")]
    [InlineData("name-length")]
    [InlineData("name-start")]
    [InlineData("description-length")]
    [InlineData("description-control")]
    public void NativeIdentityAndDisclosureBoundsCannotBeBypassed(string fault)
    {
        Action create = () => _ = new CopilotCustomizationSelection(false,
            fault is "source-length" ? new('s', 1025) : "source",
            fault is "revision-length" ? new('r', 257) : "revision",
            fault is "name-length" ? new('n', 65) : fault is "name-start" ? ".selected" : "selected.a_b-c",
            fault is "description-length" ? new('d', 1025) : fault is "description-control" ? "Hidden\ninstruction" : "Reviewed",
            "Exact source", "Exact source");
        create.Should().Throw<InvalidDataException>();
    }
}
