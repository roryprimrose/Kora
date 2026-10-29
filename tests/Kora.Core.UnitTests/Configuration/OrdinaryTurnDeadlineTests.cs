using AwesomeAssertions;

using Kora.Core.Configuration;

namespace Kora.Core.UnitTests.Configuration;

public sealed class OrdinaryTurnDeadlineTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(60)]
    public void CanonicalMinutesOwnRangeAndDuration(int minutes)
    {
        var value = OrdinaryTurnDeadline.Parse(minutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        value.Minutes.Should().Be(minutes);
        value.Duration.Should().Be(TimeSpan.FromMinutes(minutes));
        OrdinaryTurnDeadline.Default.Should().Be(new OrdinaryTurnDeadline(5));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" 5")]
    [InlineData("5 ")]
    [InlineData("+5")]
    [InlineData("05")]
    [InlineData("5.0")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("61")]
    [InlineData("2147483648")]
    public void NoncanonicalOrOutOfRangeValuesCannotCreateAnAuthorizedDeadline(string text)
    {
        var parse = () => OrdinaryTurnDeadline.Parse(text);
        parse.Should().Throw<ArgumentOutOfRangeException>();
        var invalid = () => default(OrdinaryTurnDeadline).Duration;
        invalid.Should().Throw<ArgumentOutOfRangeException>();
    }
}
