using AwesomeAssertions;
using Kora.Application.Skills;

namespace Kora.Application.UnitTests.Skills;

public sealed class OwnedSkillMarkdownCommandTests
{
    [Theory]
    [InlineData("owned skill format markdown-package", "package-format")]
    [InlineData("owned skill format unknown", "Invalid")]
    [InlineData("owned skill format markdown-package\n", "Invalid")]
    [InlineData("owned skill remove-reference notes.md", "remove-reference")]
    [InlineData("owned skill remove-reference ../notes.md", "Invalid")]
    [InlineData("owned skill remove-reference notes.md\n", "Invalid")]
    [InlineData("owned skill reference notes.md: Exact literal", "reference")]
    [InlineData("owned skill reference notes.exe: Unsupported", "Invalid")]
    [InlineData("owned skill reference : Missing path", "Invalid")]
    [InlineData("owned skill reference notes.md", "Invalid")]
    [InlineData("owned skill read-references", "read-references")]
    public void OriginalCommandsSelectOnlyBoundedExplicitComponentOperations(string input, string operation)
    {
        OwnedSkillAuthoringCommand.Parse(input, "Nova")!.Operation.Should().Be(operation);
    }

    [Fact]
    public void ActivatedComponentBodyIsLiteralExactUserDataNotAnotherCommandOrChosenPath()
    {
        var command = OwnedSkillAuthoringCommand.Parse(@"Nova, owned skill reference references\notes.md:   literal"
            + "\r\nowned skill save  ", "Nova")!;
        command.Operation.Should().Be("reference");
        command.InputName.Should().Be(@"references\notes.md");
        command.Literal.Should().Be("  literal\r\nowned skill save  ");
        OwnedSkillAuthoringCommand.FixedPhrases.Should().Contain("owned skill read-references");
        OwnedSkillAuthoringCommand.Syntax.Should().Contain("owned skill reference <relative.md>: <literal>");
    }
}
