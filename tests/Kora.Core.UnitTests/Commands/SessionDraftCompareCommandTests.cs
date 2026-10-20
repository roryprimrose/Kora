using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionDraftCompareCommandTests
{
    private const string Id = "12345678-1234-1234-1234-123456abcdef";

    [Theory]
    [InlineData("session draft compare " + Id, "Kora")]
    [InlineData("Nova, session draft compare " + Id, "Nova")]
    [InlineData("SESSION DRAFT COMPARE " + Id, "Kora")]
    [InlineData("session draft compare " + Id, "session")]
    [InlineData("  session  draft compare " + Id + "  ", "Kora")]
    public void ExactCurrentNameOrLiteralFamilyUsesCanonicalIdentity(string text, string name) =>
        SessionDraftCompareCommand.Parse(text, name).Should().Be(new SessionDraftCompareCommand(Guid.Parse(Id)));

    [Theory]
    [InlineData("session draft compare")]
    [InlineData("session draft compare!")]
    [InlineData("session draft compare\0")]
    [InlineData("session draft compare! help")]
    [InlineData("session draft compare help extra")]
    [InlineData("session draft compare " + Id + " extra")]
    [InlineData("session draft compare 12345678-1234-1234-1234-123456ABCDEF")]
    [InlineData("session draft compare 00000000-0000-0000-0000-000000000000")]
    [InlineData("session draft compare {12345678-1234-1234-1234-123456abcdef}")]
    [InlineData("session draft compare 12345678123412341234123456abcdef")]
    [InlineData("session draft compare latest")]
    [InlineData("session\tdraft compare help")]
    [InlineData("session draft compare help\n")]
    public void ReservedMalformedFamilyIsNotQuestionOrModelInput(string text) =>
        SessionDraftCompareCommand.Parse(text, "Kora").Should().Be(new SessionDraftCompareCommand());

    [Theory]
    [InlineData("")]
    [InlineData("session draft")]
    [InlineData("session draft comparison")]
    [InlineData("session list")]
    [InlineData("open sessions")]
    [InlineData("Old, session draft compare help")]
    public void UnrelatedInputIsNotClaimed(string text) =>
        SessionDraftCompareCommand.Parse(text, "Nova").Should().BeNull();

    [Fact]
    public void HelpIsConstantAndInputByteBoundIsExact()
    {
        SessionDraftCompareCommand.Parse("Nova, session draft compare help", "Nova")
            .Should().Be(new SessionDraftCompareCommand(Help: true));
        var input = "session draft compare " + Id;
        SessionDraftCompareCommand.Parse(input.PadRight(SessionCommand.MaximumInputBytes), "Kora")!.SessionId
            .Should().Be(Guid.Parse(Id));
        SessionDraftCompareCommand.Parse(input.PadRight(SessionCommand.MaximumInputBytes + 1), "Kora")
            .Should().Be(new SessionDraftCompareCommand());
        SessionDraftCompareCommand.Parse("session draft compare " + new string('\u00e9', 512), "Kora")
            .Should().Be(new SessionDraftCompareCommand());
    }
}
