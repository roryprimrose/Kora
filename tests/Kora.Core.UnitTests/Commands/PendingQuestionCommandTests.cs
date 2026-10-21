using AwesomeAssertions;
using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class PendingQuestionCommandTests
{
    private const string Session = "aaaaaaaa-1111-2222-3333-444444444444";
    private const string Task = "bbbbbbbb-1111-2222-3333-444444444444";

    [Theory]
    [InlineData("Kora")]
    [InlineData("open")]
    [InlineData("pending question")]
    public void LiteralReservedGrammarWinsOverNameCollision(string name)
    {
        var command = PendingQuestionCommand.Parse($"open pending question {Session} {Task}", name);
        command!.SessionId.Should().Be(Guid.Parse(Session));
        command.TaskId.Should().Be(Guid.Parse(Task));
        PendingQuestionCommand.Parse("pending question help", name)!.IsHelp.Should().BeTrue();
        PendingQuestionCommand.Parse($"{name}, open pending question {Session} {Task}", name)!.SessionId.Should().Be(Guid.Parse(Session));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("hello")]
    [InlineData("open sessions")]
    [InlineData("pending")]
    public void UnrelatedInputIsNotReserved(string text) =>
        PendingQuestionCommand.Parse(text, "Kora").Should().BeNull();

    [Theory]
    [InlineData("open pending")]
    [InlineData("open pending question")]
    [InlineData("pending question")]
    [InlineData("pending question help extra")]
    [InlineData("open\tpending question")]
    [InlineData("pending\nquestion help")]
    [InlineData("open pending question aaaaaaaa111122223333444444444444 bbbbbbbb-1111-2222-3333-444444444444")]
    [InlineData("open pending question AAAAAAAA-1111-2222-3333-444444444444 bbbbbbbb-1111-2222-3333-444444444444")]
    [InlineData("open pending question 00000000-0000-0000-0000-000000000000 bbbbbbbb-1111-2222-3333-444444444444")]
    [InlineData("open pending question aaaaaaaa-1111-2222-3333-444444444444 00000000-0000-0000-0000-000000000000")]
    [InlineData("open pending question  bbbbbbbb-1111-2222-3333-444444444444")]
    [InlineData("open pending question aaaaaaaa-1111-2222-3333-444444444444 ")]
    [InlineData("open pending question aaaaaaaa-1111-2222-3333-444444444444 bbbbbbbb-1111-2222-3333-444444444444 extra")]
    public void MalformedReservedInputCannotBecomeAnAnswer(string text)
    {
        var command = PendingQuestionCommand.Parse(text, "Kora");
        command.Should().NotBeNull();
        command!.SessionId.Should().BeNull();
        command.TaskId.Should().BeNull();
        command.IsHelp.Should().BeFalse();
    }

    [Fact]
    public void CurrentNamePrefixAndCaseInsensitiveCommandKeepCanonicalIds()
    {
        PendingQuestionCommand.Parse($"Nova, OPEN PENDING QUESTION {Session} {Task}", "Nova")!.TaskId.Should().Be(Guid.Parse(Task));
        PendingQuestionCommand.Parse("Nova, pending question help", "Nova")!.IsHelp.Should().BeTrue();
        PendingQuestionCommand.Parse($"open pending question  {Task}", "Kora")!.SessionId.Should().BeNull();
    }
}
