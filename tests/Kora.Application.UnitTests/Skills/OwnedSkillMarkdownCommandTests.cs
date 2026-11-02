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

    [Fact]
    public void MarkdownComponentsAndExactSourceDeletionRetainIndependentCommandRoutes()
    {
        var deletion = OwnedSkillAuthoringCommand.Parse("Nova, owned skill delete daily-review 1.0.0", "Nova")!;
        deletion.Operation.Should().Be("delete");
        deletion.Id.Should().Be("daily-review");
        deletion.Version.Should().Be("1.0.0");
        deletion.Literal.Should().BeNull();
        deletion.InputName.Should().BeNull();
        OwnedSkillAuthoringCommand.Parse("owned skill delete ../daily-review 1.0.0", "Nova")!.Operation.Should().Be("Invalid");
        OwnedSkillAuthoringCommand.Parse("owned skill reference notes.md: owned skill delete daily-review 1.0.0", "Nova")!
            .Should().Be(new OwnedSkillAuthoringCommand("reference", Literal: "owned skill delete daily-review 1.0.0", InputName: "notes.md"));
        OwnedSkillAuthoringCommand.Syntax.Should().Contain("owned skill format markdown-package")
            .And.Contain("owned skill reference <relative.md>: <literal>")
            .And.Contain("owned skill rename <old-id> <version> <new-id>")
            .And.Contain("owned skill recover-rename <old-id> <version>");
        OwnedSkillAuthoringCommand.Parse("Nova, owned skill rename daily-review 1.0.0 renamed", "Nova")!
            .Should().Be(new OwnedSkillAuthoringCommand("rename", "daily-review", "1.0.0", "renamed"));
        OwnedSkillAuthoringCommand.Syntax.Should().Contain("owned skill delete <id> <version>");
    }
}
