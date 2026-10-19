using System.Threading.Channels;

using AwesomeAssertions;

using Kora.Application.Visuals;

namespace Kora.Application.UnitTests.Visuals;

public sealed partial class PresenceRenderWorkerTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void NonNormalizedSpeechLevelsAreRejectedBeforeDrawing(double level)
    {
        using var target = new FakeTarget();
        var queue = Channel.CreateUnbounded<Action>();
        using var worker = Create(target, queue);

        var update = () => worker.Update(Inputs() with { SpeechOutputLevel = level });

        update.Should().Throw<InvalidDataException>();
        worker.Dispose();
        target.Draws.Should().Be(0);
        target.Presents.Should().Be(0);
        queue.Reader.TryRead(out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("width", double.NaN)]
    [InlineData("width", double.PositiveInfinity)]
    [InlineData("width", double.NegativeInfinity)]
    [InlineData("width", 0)]
    [InlineData("width", -1)]
    [InlineData("height", double.NaN)]
    [InlineData("height", double.PositiveInfinity)]
    [InlineData("height", double.NegativeInfinity)]
    [InlineData("height", 0)]
    [InlineData("height", -1)]
    [InlineData("scaling", double.NaN)]
    [InlineData("scaling", double.PositiveInfinity)]
    [InlineData("scaling", double.NegativeInfinity)]
    [InlineData("scaling", 0)]
    [InlineData("scaling", -1)]
    public void EachNonPositiveOrNonFiniteGeometryComponentIsRejectedBeforeDrawing(string component, double value)
    {
        using var target = new FakeTarget();
        var queue = Channel.CreateUnbounded<Action>();
        using var worker = Create(target, queue);
        var inputs = component switch
        {
            "width" => Inputs() with { Width = value },
            "height" => Inputs() with { Height = value },
            "scaling" => Inputs() with { Scaling = value },
            _ => throw new ArgumentException("Unknown geometry component.", nameof(component)),
        };

        var update = () => worker.Update(inputs);

        update.Should().Throw<InvalidDataException>();
        worker.Dispose();
        target.Draws.Should().Be(0);
        queue.Reader.TryRead(out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(4096.01, 360, 1)]
    [InlineData(360, 4096.01, 1)]
    [InlineData(2048.01, 360, 2)]
    [InlineData(360, 2048.01, 2)]
    [InlineData(double.MaxValue, 360, 2)]
    [InlineData(360, double.MaxValue, 2)]
    public void EitherScaledBufferDimensionBeyond4096PixelsIsRejected(double width, double height, double scaling)
    {
        var inputs = Inputs() with { Width = width, Height = height, Scaling = scaling };

        var validate = () => inputs.Validate();

        validate.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(4096, 4096, 1, 0)]
    [InlineData(2048, 2048, 2, 1)]
    [InlineData(8192, 8192, 0.5, 0.5)]
    public void InclusiveSpeechAndScaledBufferBoundsRemainValid(double width, double height, double scaling, double level)
    {
        var inputs = Inputs() with
        {
            Width = width,
            Height = height,
            Scaling = scaling,
            SpeechOutputLevel = level,
        };

        var validate = () => inputs.Validate();

        validate.Should().NotThrow();
    }
}
