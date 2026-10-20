using System.Text;
using AwesomeAssertions;
using Kora.Application.Numbers;
using Kora.Core.Numbers;

namespace Kora.Application.UnitTests.Numbers;

public sealed class NumberCommandTests
{
    [Theory]
    [InlineData("number help", "Kora", false)]
    [InlineData(" Kora, NUMBER HELP ", "Kora", true)]
    [InlineData("Nova number help", "Nova", true)]
    [InlineData("number help", "Number", false)]
    [InlineData("Number number help", "Number", true)]
    [InlineData("Number, number help", "Number", true)]
    public void ParsesExactHelpWithCurrentNameCollisions(string input, string name, bool activated)
    {
        var result = NumberCommand.Parse(input, name)!;
        result.IsHelp.Should().BeTrue();
        result.IsActivated.Should().Be(activated);
        result.Error.Should().BeNull();
    }

    [Theory]
    [InlineData("binary", IntegerBase.Binary)]
    [InlineData("octal", IntegerBase.Octal)]
    [InlineData("DECIMAL", IntegerBase.BaseTen)]
    [InlineData("hex", IntegerBase.Hex)]
    public void ParsesOnlyExplicitTypedBasesWithoutDoingMath(string token, IntegerBase expected)
    {
        var result = NumberCommand.Parse($"number convert -AB from {token} to hex", "Kora")!;
        result.Request.Should().Be(new IntegerConversionRequest("-AB", expected, IntegerBase.Hex));
        result.Error.Should().BeNull();
    }

    [Theory]
    [InlineData("number")]
    [InlineData("number: help")]
    [InlineData("number help extra")]
    [InlineData("number  help")]
    [InlineData("number convert 1 from hex to")]
    [InlineData("number convert 1 for hex to binary")]
    [InlineData("number convert 1 from hex in binary")]
    [InlineData("number convert 1 from 16 to binary")]
    [InlineData("number convert 1 from hex to 2")]
    [InlineData("number convert 1 from hex to binary extra")]
    [InlineData("number help\0")]
    [InlineData("\tnumber help")]
    [InlineData("number\t help")]
    [InlineData("number help\u00a0")]
    [InlineData("number convert １ from decimal to hex")]
    [InlineData("Kora:number help")]
    [InlineData("Kora: number help")]
    [InlineData("Kora,\tnumber help")]
    [InlineData("Kora , number help")]
    [InlineData("Kora,, number help")]
    [InlineData("Kora-number help")]
    [InlineData("Kora/number help")]
    [InlineData("Kora\u00a0number help")]
    public void ReservedMalformedInputNeverFallsThrough(string input) =>
        NumberCommand.Parse(input, "Kora")!.Error.Should().Be(NumberCommand.Syntax);

    [Theory]
    [InlineData("numbered help")]
    [InlineData("a number help")]
    [InlineData("Nova, number help")]
    [InlineData("Kora")]
    [InlineData("Kora,")]
    [InlineData("")]
    public void UnrelatedOrWrongNameIsNotACommand(string input) =>
        NumberCommand.Parse(input, "Kora").Should().BeNull();

    [Fact]
    public void BoundsCompleteInputBytesWithoutTruncating()
    {
        var exact = "number help".PadRight(NumberCommand.MaximumInputBytes);
        NumberCommand.Parse(exact, "Kora")!.IsHelp.Should().BeTrue();
        NumberCommand.Parse(exact + " ", "Kora")!.Error.Should().NotBeNull();
        var name = new string('\u754c', 32);
        var multibyte = name + ", number help";
        multibyte = multibyte.PadRight(multibyte.Length + NumberCommand.MaximumInputBytes - Encoding.UTF8.GetByteCount(multibyte));
        NumberCommand.Parse(multibyte, name)!.IsHelp.Should().BeTrue();
        NumberCommand.Parse(multibyte + " ", name)!.Error.Should().NotBeNull();
        NumberCommand.MaximumTokens.Should().Be(7);
        var act = () => NumberCommand.Parse(null!, "Kora");
        act.Should().Throw<ArgumentNullException>();
    }
}
