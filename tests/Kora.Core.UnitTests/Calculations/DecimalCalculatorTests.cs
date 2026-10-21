using System.Globalization;
using AwesomeAssertions;
using Kora.Core.Calculations;

namespace Kora.Core.UnitTests.Calculations;

public sealed class DecimalCalculatorTests
{
    [Theory]
    [InlineData("0", "0")]
    [InlineData(" 001.2000 ", "1.2")]
    [InlineData("1.00000000000000000000000000000000000000", "1")]
    [InlineData("1+2*3", "7")]
    [InlineData("(1 + 2) * 3", "9")]
    [InlineData("10-3-2", "5")]
    [InlineData("20/2/2", "5")]
    [InlineData("-2*-3", "6")]
    [InlineData("1--2", "3")]
    [InlineData("-(+2 + -3)", "1")]
    [InlineData("0.1+0.2", "0.3")]
    [InlineData("1/3", "0.3333333333333333333333333333")]
    [InlineData("0.0000000000000000000000000001/2", "0")]
    [InlineData("79228162514264337593543950335", "79228162514264337593543950335")]
    [InlineData("-79228162514264337593543950335.0", "-79228162514264337593543950335")]
    [InlineData("-0.0000000000000000000000000001", "-0.0000000000000000000000000001")]
    public void ArithmeticIsExactInvariantBoundedAndUsesDecimalRounding(string expression, string expected)
    {
        var result = DecimalCalculator.Evaluate(expression);
        result.Outcome.Should().Be(CalculationOutcome.Succeeded);
        result.Value.Should().Be(expected);
        result.Value!.Length.Should().BeLessThanOrEqualTo(DecimalCalculator.MaximumOutputCharacters);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".1")]
    [InlineData("1.")]
    [InlineData("1..2")]
    [InlineData("1 2")]
    [InlineData("2(3)")]
    [InlineData("()")]
    [InlineData("(1")]
    [InlineData("1)")]
    [InlineData("1+")]
    [InlineData("1/**2")]
    [InlineData("1e2")]
    [InlineData("1,2")]
    [InlineData("١")]
    [InlineData("１")]
    [InlineData("2^3")]
    [InlineData("sqrt(4)")]
    [InlineData("1; exit")]
    [InlineData("1\n+2")]
    [InlineData("1\t+2")]
    [InlineData("1\u00a0+2")]
    [InlineData("1\u202e+2")]
    public void InvalidSyntaxNeverReturnsAValue(string expression) =>
        AssertFailure(expression, CalculationOutcome.InvalidExpression);

    [Theory]
    [InlineData("79228162514264337593543950336")]
    [InlineData("79228162514264337593543950335+1")]
    [InlineData("-79228162514264337593543950335-1")]
    [InlineData("79228162514264337593543950335*2")]
    [InlineData("79228162514264337593543950335/0.1")]
    [InlineData("0.00000000000000000000000000001")]
    [InlineData("7.9228162514264337593543950336")]
    public void OverflowAndUnrepresentableLiteralsAreExplicit(string expression) =>
        AssertFailure(expression, CalculationOutcome.Overflow);

    [Theory]
    [InlineData("1/0")]
    [InlineData("0/0")]
    [InlineData("1/(2-2)")]
    public void DivisionByZeroIsNotAnAnswer(string expression) =>
        AssertFailure(expression, CalculationOutcome.DivisionByZero);

    [Fact]
    public void LimitsAdmitExactBoundariesAndRejectTheNextCharacterTokenOrFrame()
    {
        DecimalCalculator.Evaluate("1" + new string(' ', 511)).Outcome.Should().Be(CalculationOutcome.Succeeded);
        AssertFailure("1" + new string(' ', 512), CalculationOutcome.ExpressionLimit);
        var tokens = "+" + string.Join("+", Enumerable.Repeat("1", 64));
        DecimalCalculator.Evaluate(tokens).Value.Should().Be("64");
        AssertFailure(tokens + "+1", CalculationOutcome.TokenLimit);
        foreach (var unary in new[] { false, true })
        {
            string Nested(int count) => unary ? new string('-', count) + "1"
                : new string('(', count) + "1" + new string(')', count);
            DecimalCalculator.Evaluate(Nested(16)).Value.Should().Be("1");
            AssertFailure(Nested(17), CalculationOutcome.NestingLimit);
        }
        AssertFailure(new string('(', 9) + new string('-', 8) + "1", CalculationOutcome.NestingLimit);
    }

    [Fact]
    public void CultureNeverChangesSyntaxOrOutput()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            DecimalCalculator.Evaluate("1.5 + 2").Value.Should().Be("3.5");
            AssertFailure("1,5", CalculationOutcome.InvalidExpression);
        }
        finally { CultureInfo.CurrentCulture = previous; }
        Action nullInput = () => DecimalCalculator.Evaluate(null!);
        nullInput.Should().Throw<ArgumentNullException>();
    }

    private static void AssertFailure(string expression, CalculationOutcome outcome)
    {
        var result = DecimalCalculator.Evaluate(expression);
        result.Outcome.Should().Be(outcome);
        result.Value.Should().BeNull();
    }
}
