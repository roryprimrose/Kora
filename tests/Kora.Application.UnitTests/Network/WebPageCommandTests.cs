using AwesomeAssertions;
using Kora.Application.Network;

namespace Kora.Application.UnitTests.Network;

public sealed class WebPageCommandTests
{
    [Theory]
    [InlineData("get web page https://example.com/page")]
    [InlineData("Nova, get web page https://example.com/page")]
    [InlineData("Nova get web page https://example.com/page")]
    public void ExactTypedAndVoiceCommandCapturesTheAddress(string input)
    {
        var command = WebPageCommand.Parse(input, "Nova");

        command!.Error.Should().BeNull();
        command.Address.Should().Be(new Uri("https://example.com/page"));
    }

    [Theory]
    [InlineData("get web page")]
    [InlineData("get web page ftp://example.com/file")]
    [InlineData("get web page https://user@example.com/")]
    [InlineData("get web page https://example.com/#fragment")]
    [InlineData("get web page relative")]
    public void InvalidOrUnsupportedAddressesAreClarified(string input) =>
        WebPageCommand.Parse(input, "Kora")!.Error.Should().Be(WebPageCommand.Syntax);

    [Fact]
    public void UnrelatedRequestIsNotClaimed() =>
        WebPageCommand.Parse("tell me about a web page", "Kora").Should().BeNull();
}
