using AwesomeAssertions;
using Kora.Application.Configuration;

namespace Kora.Application.UnitTests.Configuration;

public sealed class ResponseWindowCommandTests
{
    [Fact]
    public void EveryFixedPhraseParsesExactlyWithCurrentNameAndCaseInsensitiveMatching()
    {
        foreach (var phrase in ResponseWindowCommand.FixedPhrases)
        {
            var command = ResponseWindowCommand.Parse(phrase, "Nova Prime");
            command.Should().NotBeNull();
            command!.Operation.Should().NotBe(AppearanceCommandOperation.Clarify);
            ResponseWindowCommand.Parse("  NOVA PRIME, " + phrase.ToUpperInvariant() + "  ", "Nova Prime")
                .Should().Be(command);
            ResponseWindowCommand.Parse("Nova Prime " + phrase, "Nova Prime").Should().Be(command);
        }
    }

    [Theory]
    [InlineData("keep responses visible", ResponseWindowOption.AlwaysShow, true)]
    [InlineData("allow responses to hide", ResponseWindowOption.AlwaysShow, false)]
    [InlineData("keep response window on top", ResponseWindowOption.Topmost, true)]
    [InlineData("stop keeping response window on top", ResponseWindowOption.Topmost, false)]
    public void NaturalPhrasesAddressOnlyOneExplicitBoolean(string text, ResponseWindowOption option, bool value)
    {
        ResponseWindowCommand.Parse(text, "Kora").Should()
            .Be(new ResponseWindowCommand(AppearanceCommandOperation.Set, option, value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Kora")]
    [InlineData("KoraX keep responses visible")]
    [InlineData("Nova, keep responses visible")]
    [InlineData("please keep responses visible")]
    [InlineData("tell me about keep responses visible")]
    [InlineData("keep that on top")]
    [InlineData("set responses.default-mode to Hybrid")]
    [InlineData("set appearance.theme to dark")]
    public void UnrelatedOrUnaddressedTextDoesNotMatch(string input)
    {
        ResponseWindowCommand.Parse(input, "Kora").Should().BeNull();
    }

    [Theory]
    [InlineData("list response window")]
    [InlineData("get response-window")]
    [InlineData("status response-window.topmost please")]
    [InlineData("set response-window.topmost to yes")]
    [InlineData("reset response-window.always-show and topmost")]
    [InlineData("get response window position")]
    [InlineData("status response window topmost now")]
    [InlineData("set response window topmost")]
    [InlineData("reset response window")]
    [InlineData("keep responses visible and open the old response")]
    [InlineData("allow responses to hide now")]
    [InlineData("keep response window on top of every application forever")]
    [InlineData("stop keeping response window on top and reset position")]
    [InlineData("set response-window.always-show to false\n")]
    [InlineData("Kora,\tget response-window.topmost")]
    [InlineData("set response-window.topmost to tr\u0000ue")]
    public void MalformedReservedInputClarifiesWithoutPartialMatch(string input)
    {
        ResponseWindowCommand.Parse(input, "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
    }

    [Fact]
    public void OversizeReservedInputClarifiesWithoutTruncation()
    {
        ResponseWindowCommand.Parse("set response-window.topmost to " + new string('x', 256), "Kora")!
            .Operation.Should().Be(AppearanceCommandOperation.Clarify);
        var exact = "get response-window.topmost";
        ResponseWindowCommand.Parse(exact.PadRight(256), "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Get);
        ResponseWindowCommand.Parse(exact.PadRight(257), "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
    }
}
