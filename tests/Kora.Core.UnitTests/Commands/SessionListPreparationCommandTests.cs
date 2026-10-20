using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionListPreparationCommandTests
{
    private const string Id = "abcdef01-2345-6789-abcd-ef0123456789";

    [Theory]
    [InlineData("prepare session list ", "Kora", false)]
    [InlineData("Kora, prepare session list ", "Kora", true)]
    [InlineData("Nova prepare session list ", "Nova", true)]
    [InlineData("PREPARE SESSION LIST ", "prepare", false)]
    [InlineData("prepare session list ", "session list help", false)]
    [InlineData("prepare session list, prepare session list ", "prepare session list", true)]
    [InlineData("session list help, prepare session list ", "session list help", true)]
    public void OnlyCanonicalNonemptyIdsAreQueryData(string prefix, string name, bool activated)
    {
        var parsed = SessionListPreparationCommand.Parse(prefix + Id, name)!;
        parsed.IsValid.Should().BeTrue();
        parsed.IsHelp.Should().BeFalse();
        parsed.IsActivated.Should().Be(activated);
        parsed.SessionId.Should().Be(Guid.Parse(Id));
        parsed.ToString().Should().Be(nameof(SessionListPreparationCommand)).And.NotContain(Id);
        SessionListPreparationCommand.DiscoveryPhrases.Should().Equal("prepare session list", "session list help");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("open sessions")]
    [InlineData("session list")]
    [InlineData("session list limit 25")]
    [InlineData("session inspect abc")]
    [InlineData("prepare file search for \"words\"")]
    [InlineData("prepare session list", "Nova")]
    public void UnrelatedAndExistingRevisionedGrammarIsNotReinterpreted(string input, string name = "Kora")
    {
        if (input is "prepare session list") { input = "Kora, " + input; }
        SessionListPreparationCommand.Parse(input, name).Should().BeNull();
    }

    [Theory]
    [InlineData("prepare session")]
    [InlineData("prepare session list")]
    [InlineData("prepare session list ")]
    [InlineData("prepare-session-list current")]
    [InlineData("prepare session list latest")]
    [InlineData("prepare session list \"name\"")]
    [InlineData("prepare session list 00000000-0000-0000-0000-000000000000")]
    [InlineData("prepare session list ABCDEF01-2345-6789-ABCD-EF0123456789")]
    [InlineData("prepare session list abcdef0123456789abcdef0123456789")]
    [InlineData("prepare session list {abcdef01-2345-6789-abcd-ef0123456789}")]
    [InlineData("prepare session list  abcdef01-2345-6789-abcd-ef0123456789")]
    [InlineData("prepare session list abcdef01-2345-6789-abcd-ef0123456789 tail")]
    [InlineData("prepare session list abcdef01-2345-6789-abcd-ef0123456789.")]
    [InlineData("prepare session list \u202eabcdef01-2345-6789-abcd-ef0123456789")]
    [InlineData("prepare session list \u200babcdef01-2345-6789-abcd-ef0123456789")]
    [InlineData("prepare session list \ud800")]
    [InlineData("prepare\tsession list abcdef01-2345-6789-abcd-ef0123456789")]
    [InlineData("prepare session\nlist abcdef01-2345-6789-abcd-ef0123456789")]
    [InlineData("session list help!")]
    [InlineData("session-list-help")]
    [InlineData("session list help current")]
    [InlineData("Kora, prepare session list current")]
    public void ReservedMalformedFamiliesNeverFallThrough(string input)
    {
        var command = SessionListPreparationCommand.Parse(input, "Kora")!;
        command.Should().NotBeNull();
        command.IsValid.Should().BeFalse();
        command.IsHelp.Should().BeFalse();
        command.SessionId.Should().BeNull();
    }

    [Theory]
    [InlineData(1024, false)]
    [InlineData(1025, false)]
    [InlineData(1000, true)]
    public void InputWorkAndUtf8AreBounded(int size, bool unicode)
    {
        var input = "prepare session list " + new string(unicode ? '\u754c' : 'x', size - 21);
        SessionListPreparationCommand.Parse(input, "Kora")!.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("session list help", "session", false)]
    [InlineData("Nova, session list help", "Nova", true)]
    public void HelpIsConstantAndDoesNotInferAnId(string input, string name, bool activated)
    {
        var help = SessionListPreparationCommand.Parse(input, name)!;
        help.IsValid.Should().BeTrue();
        help.IsHelp.Should().BeTrue();
        help.SessionId.Should().BeNull();
        help.IsActivated.Should().Be(activated);
        SessionListPreparationCommand.Syntax.Should().Contain("AND").And.Contain("no Search has run");
    }

    [Fact]
    public void RequiredCanonicalGuidReusesOptionalValidationButNeverTreatsEmptyAsAny()
    {
        var empty = () => Kora.Core.Hosting.OptionalCanonicalGuid.ParseRequired(string.Empty);
        empty.Should().Throw<InvalidDataException>();
        Kora.Core.Hosting.OptionalCanonicalGuid.ParseRequired(Id).Should().Be(Guid.Parse(Id));
    }
}
