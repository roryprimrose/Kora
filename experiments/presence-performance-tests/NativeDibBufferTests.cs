using AwesomeAssertions;

using SkiaSharp;

namespace Presence.Performance.Tests;

public sealed class NativeDibBufferTests
{
    [Theory]
    [InlineData(3, 2)]
    [InlineData(7, 5)]
    [InlineData(240, 3)]
    public void DibBytesHaveTopDownBgraLayoutAndPremultipliedAlpha(int width, int height)
    {
        using var buffer = new NativeDibBuffer(width, height);
        buffer.ReadLayout().Should().Be(new NativeDibBuffer.Layout(width, height, width * 4, 32, 0));
        buffer.DeviceContext.Should().NotBe(0);
        buffer.Draw(canvas =>
        {
            using var paint = new SKPaint { Color = new SKColor(200, 100, 50, 128) };
            canvas.DrawRect(0, 0, 1, 1, paint);
            paint.Color = new SKColor(17, 33, 201, 255);
            canvas.DrawRect(width - 1, height - 1, 1, 1, paint);
        });

        var bytes = buffer.CopyBgraPixels();
        bytes.Length.Should().Be(width * height * 4);
        bytes.AsSpan(0, 4).ToArray().Should().Equal([25, 50, 100, 128]);
        bytes.AsSpan(bytes.Length - 4, 4).ToArray().Should().Equal([201, 33, 17, 255]);
        bytes.AsSpan(4, bytes.Length - 8).ToArray().Should().OnlyContain(value => value == 0);
        buffer.ReadGdiRgb(0, 0).Should().Be(0x00193264u);
        buffer.ReadGdiRgb(width - 1, height - 1).Should().Be(0x00C92111u);
    }

    [Fact]
    public void ReusingTheBufferClearsAllPreviousPixels()
    {
        using var buffer = new NativeDibBuffer(3, 5);
        var dc = buffer.DeviceContext;
        buffer.Draw(canvas => canvas.Clear(SKColors.Red));
        buffer.CopyBgraPixels().Should().Contain(value => value != 0);
        buffer.Draw(_ => { });
        buffer.CopyBgraPixels().Should().OnlyContain(value => value == 0);
        buffer.DeviceContext.Should().Be(dc);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    [InlineData(-1, 2)]
    [InlineData(2, -1)]
    public void InvalidDimensionsAreRejectedBeforeAllocatingNativeResources(int width, int height)
    {
        var create = () =>
        {
            using var buffer = new NativeDibBuffer(width, height);
        };
        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void OverflowingDimensionsAreRejectedBeforeAllocatingNativeResources()
    {
        var create = () =>
        {
            using var buffer = new NativeDibBuffer(int.MaxValue, 2);
        };
        create.Should().Throw<OverflowException>();
    }

    [Fact]
    public void DisposeIsIdempotentAndRejectsSubsequentNativeAccess()
    {
        var buffer = new NativeDibBuffer(3, 5);
        buffer.Dispose();
        buffer.Dispose();

        var read = () => buffer.CopyBgraPixels();
        var layout = () => buffer.ReadLayout();
        var dc = () => buffer.DeviceContext;
        var snapshot = () => buffer.Snapshot();
        var draw = () => buffer.Draw(_ => { });
        var gdiRead = () => buffer.ReadGdiRgb(0, 0);
        read.Should().Throw<ObjectDisposedException>();
        layout.Should().Throw<ObjectDisposedException>();
        dc.Should().Throw<ObjectDisposedException>();
        snapshot.Should().Throw<ObjectDisposedException>();
        draw.Should().Throw<ObjectDisposedException>();
        gdiRead.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void RepeatedBufferAllocationReleasesItsGdiObjects()
    {
        using (var warmup = new NativeDibBuffer(3, 5))
        {
            warmup.Draw(_ => { });
        }
        var before = ResourceSnapshot.Capture();
        for (var index = 0; index < 100; index++)
        {
            using var buffer = new NativeDibBuffer(3, 5);
            buffer.Draw(canvas => canvas.Clear(SKColors.Red));
        }
        ResourceSnapshot.Capture().GdiObjects.Should().Be(before.GdiObjects);
    }
}