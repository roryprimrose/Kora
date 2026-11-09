using AwesomeAssertions;
using Kora.Core.Configuration;

namespace Kora.Core.UnitTests.Configuration;

public sealed class WorkbenchPlacementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Two_mode_geometry_round_trips_without_private_content(bool maximized)
    {
        var placement = new WorkbenchPlacement(new(-800, -500, 480.5, 360, 1.25),
            new(120, 30, 1280, 820.5, 2), maximized);
        WorkbenchPlacement.Parse(placement.Serialize()).Should().Be(placement);
    }

    [Theory]
    [InlineData("1\n-\n-\nNormal")]
    [InlineData("1\n0|0|480|360|1\n-\nNormal")]
    [InlineData("1\n-\n0|0|1280|820|1\nMaximized")]
    public void Partial_placement_keeps_unobserved_modes_absent(string text) =>
        WorkbenchPlacement.Parse(text).Serialize().Should().Be(text);

    [Theory]
    [InlineData("")]
    [InlineData("2\n-\n-\nNormal")]
    [InlineData("1\n-\n-\nMinimized")]
    [InlineData("1\n-\n-\nNormal\nextra")]
    [InlineData("1\nbad\n-\nNormal")]
    [InlineData("1\nx|0|480|360|1\n-\nNormal")]
    [InlineData("1\n0|y|480|360|1\n-\nNormal")]
    [InlineData("1\n0|0|wide|360|1\n-\nNormal")]
    [InlineData("1\n0|0|480|high|1\n-\nNormal")]
    [InlineData("1\n0|0|480|360|scale\n-\nNormal")]
    [InlineData("1\n0|0|0|360|1\n-\nNormal")]
    [InlineData("1\n0|0|480|0|1\n-\nNormal")]
    [InlineData("1\n0|0|480|360|0\n-\nNormal")]
    [InlineData("1\n0|0|1e999|360|1\n-\nNormal")]
    public void Malformed_saved_placement_is_not_defaulted(string text) =>
        FluentActions.Invoking(() => WorkbenchPlacement.Parse(text)).Should().Throw<InvalidDataException>();

    [Theory]
    [InlineData(double.NaN, 360, 1)]
    [InlineData(double.PositiveInfinity, 360, 1)]
    [InlineData(0, 360, 1)]
    [InlineData(32769, 360, 1)]
    [InlineData(480, double.NaN, 1)]
    [InlineData(480, 0, 1)]
    [InlineData(480, 32769, 1)]
    [InlineData(480, 360, double.NaN)]
    [InlineData(480, 360, 0)]
    [InlineData(480, 360, 17)]
    public void Invalid_dimensions_are_rejected(double width, double height, double scale) =>
        FluentActions.Invoking(() => new WorkbenchBounds(0, 0, width, height, scale))
            .Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void Null_input_is_rejected() =>
        FluentActions.Invoking(() => WorkbenchPlacement.Parse(null!)).Should().Throw<ArgumentNullException>();
}
