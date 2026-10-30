using AwesomeAssertions;
using Kora.Application.Configuration;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LogonStartupCommandTests
{
    [Theory]
    [InlineData("check start at logon", LogonStartupOperation.Inspect)]
    [InlineData("enable start at logon", LogonStartupOperation.Enable)]
    [InlineData("disable start at logon", LogonStartupOperation.Disable)]
    [InlineData("reset start at logon", LogonStartupOperation.Reset)]
    [InlineData("confirm start at logon change", LogonStartupOperation.Confirm)]
    [InlineData("cancel start at logon review", LogonStartupOperation.Cancel)]
    public void ExactGrammarAndCurrentNamePrefixesHaveOneMeaning(string phrase, LogonStartupOperation operation)
    {
        LogonStartupCommand.FixedPhrases.Should().Contain(phrase);
        foreach (var prefix in new[] { "", "Kora ", "Kora, " })
        { LogonStartupCommand.Parse(" " + prefix + phrase.ToUpperInvariant() + " ", "Kora").Should().Be(operation); }
    }

    [Theory]
    [InlineData("enable start at logon and listen")]
    [InlineData("confirm start at logon change!")]
    [InlineData("Other, enable start at logon")]
    [InlineData("enable start at logon\n")]
    public void ReservedMalformedInputNeverFallsThrough(string input) =>
        LogonStartupCommand.Parse(input, "Kora").Should().Be(LogonStartupOperation.Invalid);

    [Fact]
    public void UnrelatedAndOversizedInputAreNotAccepted()
    {
        LogonStartupCommand.Parse("hello", "Kora").Should().BeNull();
        LogonStartupCommand.Parse("enable start at logon" + new string(' ', 300), "Kora").Should().Be(LogonStartupOperation.Invalid);
    }
}
