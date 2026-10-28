using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed partial class WorkspaceSurfaceCommandTests
{
    [Theory]
    [InlineData("show sessions needing an answer")]
    [InlineData("Nova, SHOW SESSIONS NEEDING AN ANSWER")]
    public void WaitingQuestionsIsAnExactReadOperationNotPassiveOpening(string input)
    {
        var result = WorkspaceSurfaceCommand.Parse(input, "Nova")!;
        result.Target.Should().Be(WorkspaceSurfaceTarget.WaitingQuestions);
        result.SessionPosition.Should().BeNull();
        result.ConversationName.Should().BeNull();
        WorkspaceSurfaceCommand.FixedPhrases.Should().Contain("show sessions needing an answer");
        WorkspaceSurfaceCommand.Parse("Kora, show sessions needing an answer", "Nova").Should().BeNull();
        WorkspaceSurfaceCommand.Parse("open all work", "Nova")!.Target.Should().Be(WorkspaceSurfaceTarget.AllWork);
    }

    [Theory]
    [InlineData("show sessions needing an answer extra")]
    [InlineData("show sessions needing an answer!")]
    [InlineData("show sessions needing an answer\n")]
    [InlineData("show sessions needing an answer; approve")]
    [InlineData("show sessions needing")]
    public void MalformedWaitingRequestsClarifyRatherThanFallThrough(string input) =>
        WorkspaceSurfaceCommand.Parse(input, "Nova")!.Target.Should().Be(WorkspaceSurfaceTarget.Invalid);
}
