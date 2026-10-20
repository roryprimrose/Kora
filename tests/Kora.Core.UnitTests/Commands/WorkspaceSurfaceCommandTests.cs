using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class WorkspaceSurfaceCommandTests
{
    [Theory]
    [InlineData("open all work", WorkspaceSurfaceTarget.AllWork)]
    [InlineData("open saved conversation drafts", WorkspaceSurfaceTarget.SavedConversationDrafts)]
    [InlineData("open compact conversation", WorkspaceSurfaceTarget.CompactConversation)]
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
    [InlineData("open saved conversation drafts search \"private body\"")]
    [InlineData("open saved conversation drafts\0")]
    [InlineData("open compact")]
    [InlineData("open compact conversation selected")]
    [InlineData("open compact conversation; resume")]
    public void ReservedMalformedInputIsNotFreeForm(string phrase)
    {
        WorkspaceSurfaceCommand.Parse("Nova, " + phrase, "Nova")!.Target.Should().Be(WorkspaceSurfaceTarget.Invalid);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Nova")]
    [InlineData("open sessions")]
    [InlineData("open new request")]
    [InlineData("please open all work")]
    public void UnownedInputRemainsOutsideNavigation(string input) =>
        WorkspaceSurfaceCommand.Parse(input, "Nova").Should().BeNull();

    [Theory]
    [InlineData("All Work")]
    [InlineData("Saved Conversation Drafts")]
    [InlineData("Compact Conversation")]
    public void AssistantNamesCannotStealNavigation(string name)
    {
        var action = () => new BuiltInCommandCatalog().GetCommands(name);
        action.Should().Throw<ArgumentException>().WithMessage("*conflicts*navigation*");
    }
}
