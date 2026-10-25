using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class WorkspaceSurfaceCommandTests
{
    [Theory]
    [InlineData("open all work", WorkspaceSurfaceTarget.AllWork)]
    [InlineData("open compact conversation", WorkspaceSurfaceTarget.CompactConversation)]
    [InlineData("collapse conversation", WorkspaceSurfaceTarget.CompactConversation)]
    [InlineData("open sessions", WorkspaceSurfaceTarget.Sessions)]
    [InlineData("show sessions", WorkspaceSurfaceTarget.Sessions)]
    [InlineData("show me the current sessions", WorkspaceSurfaceTarget.Sessions)]
    [InlineData("expand conversation", WorkspaceSurfaceTarget.Sessions)]
    [InlineData("show me the detailed results", WorkspaceSurfaceTarget.Details)]
    [InlineData("show detailed results", WorkspaceSurfaceTarget.Details)]
    [InlineData("show details", WorkspaceSurfaceTarget.Details)]
    [InlineData("hide details", WorkspaceSurfaceTarget.HideDetails)]
    [InlineData("show session logs", WorkspaceSurfaceTarget.SessionLogs)]
    [InlineData("show session audit", WorkspaceSurfaceTarget.SessionAudit)]
    public void ExactRoutesAreTypedAndCurrentNameQualified(string phrase, WorkspaceSurfaceTarget target)
    {
        WorkspaceSurfaceCommand.Parse(phrase, "Nova Prime")!.Target.Should().Be(target);
        WorkspaceSurfaceCommand.Parse(" Nova Prime, " + phrase.ToUpperInvariant() + " ", "Nova Prime")!.Target.Should().Be(target);
        WorkspaceSurfaceCommand.Parse("Kora, " + phrase, "Nova Prime").Should().BeNull();
        WorkspaceSurfaceCommand.Parse(phrase, "Open")!.Target.Should().Be(target);
        new BuiltInCommandRouter(new()).Match(phrase, "Nova Prime").IsMatch.Should().BeFalse();
        WorkspaceSurfaceCommand.FixedPhrases.Should().Contain(phrase);
    }

    [Theory]
    [InlineData("open all work selected")]
    [InlineData("open all work!")]
    [InlineData("open all-work")]
    [InlineData("open\tall work")]
    [InlineData("open all work\n")]
    [InlineData("open saved conversation")]
    [InlineData("open saved conversation drafts")]
    [InlineData("open saved conversation drafts search \"private body\"")]
    [InlineData("open saved conversation drafts\0")]
    [InlineData("open compact")]
    [InlineData("open compact conversation selected")]
    [InlineData("open compact conversation; resume")]
    [InlineData("select the session")]
    [InlineData("select the third session and approve")]
    [InlineData("select the twenty-sixth session")]
    [InlineData("select the first session\n")]
    [InlineData("show sessions!")]
    [InlineData("open sessions extra")]
    [InlineData("show me the current sessions extra")]
    [InlineData("expand conversation extra")]
    [InlineData("collapse conversation extra")]
    [InlineData("show details extra")]
    [InlineData("show detailed results!")]
    [InlineData("show me the detailed results extra")]
    [InlineData("hide details extra")]
    [InlineData("show session logs extra")]
    [InlineData("show session audit extra")]
    public void ReservedMalformedInputIsNotFreeForm(string phrase)
    {
        WorkspaceSurfaceCommand.Parse("Nova, " + phrase, "Nova")!.Target.Should().Be(WorkspaceSurfaceTarget.Invalid);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Nova")]
    [InlineData("open new request")]
    [InlineData("please open all work")]
    [InlineData("select the file")]
    public void UnownedInputRemainsOutsideNavigation(string input) =>
        WorkspaceSurfaceCommand.Parse(input, "Nova").Should().BeNull();

    [Fact]
    public void EveryOrdinalSelectsTheSameOneBasedPresentedPosition()
    {
        var phrases = WorkspaceSurfaceCommand.FixedPhrases
            .Where(phrase => phrase.StartsWith("select the ", StringComparison.Ordinal)).ToArray();
        phrases.Should().HaveCount(27);
        for (var index = 0; index < 25; index++)
        {
            var result = WorkspaceSurfaceCommand.Parse("Nova, " + phrases[index].ToUpperInvariant(), "Nova")!;
            result.Target.Should().Be(WorkspaceSurfaceTarget.SelectSession);
            result.SessionPosition.Should().Be(index + 1);
        }
        WorkspaceSurfaceCommand.Parse("select the top session", "Nova")!.SessionPosition.Should().Be(1);
        WorkspaceSurfaceCommand.Parse("select the last session", "Nova")!.SessionPosition
            .Should().Be(WorkspaceSurfaceCommand.LastSessionPosition);
        WorkspaceSurfaceCommand.Parse("select the last session\0", "Nova")!.Target.Should().Be(WorkspaceSurfaceTarget.Invalid);
        WorkspaceSurfaceCommand.Parse("select the\tthird session", "Nova")!.Target.Should().Be(WorkspaceSurfaceTarget.Invalid);
    }

    [Theory]
    [InlineData("All Work")]
    [InlineData("Compact Conversation")]
    public void AssistantNamesCannotStealNavigation(string name)
    {
        var action = () => new BuiltInCommandCatalog().GetCommands(name);
        action.Should().Throw<ArgumentException>().WithMessage("*conflicts*navigation*");
    }
}
