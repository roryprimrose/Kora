using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class NewRequestOpenCommandTests
{
    [Theory]
    [InlineData("open new request", "Kora", false)]
    [InlineData("  OPEN NEW REQUEST  ", "Nova", false)]
    [InlineData("Nova, open new request", "Nova", true)]
    [InlineData("North Star open new request", "North Star", true)]
    [InlineData("O'Brien: open new request", "O'Brien", true)]
    [InlineData("open new request", "Open", false)]
    public void ParseAcceptsOnlyExactNavigationAndCurrentPrefix(string input, string name, bool prefixed)
    {
        NewRequestOpenCommand.Parse(input, name).Should().Be(new NewRequestOpenCommand(true, prefixed));
        NewRequestOpenCommand.FixedPhrases.Should().Equal("open new request");
    }

    [Theory]
    [InlineData("open new request now")]
    [InlineData("open new request selected")]
    [InlineData("open new request; submit")]
    [InlineData("open new request \"body\"")]
    [InlineData("open new requests")]
    [InlineData("open-new-request")]
    [InlineData("open new request.")]
    [InlineData("open  new request")]
    [InlineData("open new request\n")]
    [InlineData("open\tnew request")]
    [InlineData("Nova\topen new request")]
    [InlineData("Nova! open new request")]
    [InlineData("Nova, open new request\u0000")]
    public void ParseReservesMalformedInputWithoutAcceptingEffects(string input)
    {
        var command = NewRequestOpenCommand.Parse(input, "Nova");
        command.Should().NotBeNull();
        command!.Accepted.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("new request")]
    [InlineData("open sessions")]
    [InlineData("session create \"New request\"")]
    [InlineData("explain open new request")]
    [InlineData("Kora, open new request")]
    [InlineData("Novation open new request")]
    public void ParseDoesNotInventAliasesOrObsoletePrefixes(string input)
    {
        NewRequestOpenCommand.Parse(input, "Nova").Should().BeNull();
    }
}
