using AwesomeAssertions;

namespace Presence.Performance.Tests;

public sealed class BgraPixelComparisonTests
{
    [Fact]
    public void IdenticalPremultipliedBytesHaveNoError()
    {
        byte[] pixels = [25, 50, 100, 128, 201, 33, 17, 255];
        var result = BgraPixelComparison.Compare(pixels, pixels);
        result.ComparedBytes.Should().Be(8);
        result.ChangedPixels.Should().Be(0);
        result.MaximumChannelError.Should().Be(0);
        result.AlphaRelativeError.Should().Be(0);
        result.ChannelRelativeError.Should().Be(0);
        result.InvalidPremultipliedPixels.Should().Be(0);
    }

    [Theory]
    [InlineData("swapped-channels")]
    [InlineData("bottom-up")]
    [InlineData("straight-alpha")]
    public void ByteComparisonDetectsLayoutAndAlphaRegressions(string mutation)
    {
        byte[] expected = [25, 50, 100, 128, 201, 33, 17, 255];
        byte[] actual = mutation switch
        {
            "swapped-channels" => [100, 50, 25, 128, 17, 33, 201, 255],
            "bottom-up" => [201, 33, 17, 255, 25, 50, 100, 128],
            "straight-alpha" => [50, 100, 200, 128, 201, 33, 17, 255],
            _ => throw new ArgumentException("Unknown test mutation.", nameof(mutation)),
        };
        var result = BgraPixelComparison.Compare(expected, actual);
        result.ChangedPixels.Should().BeGreaterThan(0);
        result.MaximumChannelError.Should().BeGreaterThan(1);
        result.ChannelRelativeError.Should().BeGreaterThan(0.01);
        if (mutation is "straight-alpha")
        {
            result.InvalidPremultipliedPixels.Should().Be(1);
        }
    }

    [Fact]
    public void TransparentPixelsCannotHideRgbCorruption()
    {
        var result = BgraPixelComparison.Compare(new byte[4], [12, 0, 0, 0]);
        result.MaximumChannelError.Should().Be(12);
        result.InvalidPremultipliedPixels.Should().Be(1);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(4, 8)]
    public void MalformedOrMismatchedImagesAreRejected(int expectedLength, int actualLength)
    {
        var compare = () => BgraPixelComparison.Compare(new byte[expectedLength], new byte[actualLength]);
        compare.Should().Throw<ArgumentException>();
    }
}