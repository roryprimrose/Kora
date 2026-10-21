using AwesomeAssertions;
using Kora.Core.Calculations;

namespace Kora.Core.UnitTests.Calculations;

public sealed class CalculateCommandTests
{
    [Theory]
    [InlineData("calculate 1 + 2", false)]
    [InlineData(" Nova-Prime, calculate 1 + 2 ", true)]
    [InlineData("NOVA-PRIME calculate 1 + 2", true)]
    public void RawExpressionAndCurrentNameArePreserved(string input, bool activated)
    {
        var result = CalculateCommand.Parse(input, "Nova-Prime")!;
        result.Expression.Should().Be("1 + 2");
        result.IsActivated.Should().Be(activated);
        result.IsHelp.Should().BeFalse();
        result.ToString().Should().Be(nameof(CalculateCommand));
    }

    [Theory]
    [InlineData("Nova-Prime")]
    [InlineData("Nova-PrimeX calculate 1")]
    [InlineData("Kora, calculate 1")]
    [InlineData("Nova, calculate 1")]
    [InlineData("what is 1 + 2")]
    [InlineData("")]
    public void OldPartialAndUnrelatedPhrasesAreNotAliases(string input) =>
        CalculateCommand.Parse(input, "Nova-Prime").Should().BeNull();

    [Theory]
    [InlineData("calculate")]
    [InlineData("calculate(1+2)")]
    [InlineData("calculate-help")]
    [InlineData("calculateplease")]
    [InlineData("calculate\t1")]
    [InlineData("calculate 1\n")]
    [InlineData("calculate help\0")]
    public void MalformedReservedInputRemainsReservedWithoutAnExpression(string input)
    {
        var result = CalculateCommand.Parse(input, "Kora")!;
        result.Expression.Should().BeNull();
        result.IsHelp.Should().BeFalse();
    }

    [Fact]
    public void HelpAndInputBoundsAreExplicit()
    {
        CalculateCommand.Parse("Kora, CALCULATE HELP", "Kora")!.IsHelp.Should().BeTrue();
        CalculateCommand.Parse("calculate " + new string('1', CalculateCommand.MaximumInputCharacters), "Kora")!
            .Expression.Should().BeNull();
        CalculateCommand.Parse("calculate " + new string('1', CalculateCommand.MaximumInputCharacters - 10), "Kora")!
            .Expression.Should().NotBeNull();
        CalculateCommand.DiscoveryPhrases.Should().Equal("calculate", "calculate help");
    }

    [Fact]
    public void CommandWordAsAValidAssistantNameDoesNotSwallowTypedArithmetic()
    {
        CalculateCommand.Parse("calculate 1+2", "Calculate")!.Expression.Should().Be("1+2");
        CalculateCommand.Parse("calculate 1+2", "Calculate")!.IsActivated.Should().BeFalse();
        CalculateCommand.Parse("Calculate, calculate 1+2", "Calculate")!.IsActivated.Should().BeTrue();
        CalculateCommand.Parse("Nova-Prime, tell me something", "Nova-Prime").Should().BeNull();
    }
}
