using AwesomeAssertions;

using Kora.Controls;
using Kora.Core.Presentation;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Presentation;

using SkiaSharp;

namespace Kora.Windows.IntegrationTests.Presentation;

public sealed class NativePresenceTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("avalonia", false)]
    [InlineData("native", true)]
    public void RendererSelectionRequiresExactExplicitOptIn(string? value, bool expected) =>
        PresenceRendererSelection.UseNative(value).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("Native")]
    [InlineData("true")]
    [InlineData("unknown")]
    public void UnknownRendererSelectionIsNotSilentlyDefaulted(string value)
    {
        var select = () => PresenceRendererSelection.UseNative(value);
        select.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(3, 5, 1d)]
    [InlineData(240, 240, 1d)]
    [InlineData(360, 360, 1.5)]
    [InlineData(600, 600, 2d)]
    public void ProductionDibBytesMatchIndependentPremultipliedSkiaReference(int width, int height, double scaling)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows DIB allocation is required.");
        }
        var dots = new[]
        {
            new PresenceParticle(1, 1, 0.8, 0),
            new PresenceParticle(width / scaling - 1, height / scaling - 1, 0.7, 5),
        };
        using var dib = new WindowsPresenceDib(width, height);
        dib.Draw(dots, scaling, 200, 100, 50, 0.7);
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var reference = SKSurface.Create(info);
        reference.Canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { IsAntialias = true };
        foreach (var dot in dots)
        {
            paint.Color = new SKColor(200, 100, 50, (byte)Math.Round((70 + dot.Depth * 28) * 0.7));
            reference.Canvas.DrawCircle((float)(dot.X * scaling), (float)(dot.Y * scaling),
                (float)(dot.Radius * scaling), paint);
        }
        using var bitmap = new SKBitmap(info);
        reference.ReadPixels(info, bitmap.GetPixels(), dib.Stride, 0, 0).Should().BeTrue();
        dib.CopyPixels().Should().Equal(bitmap.Bytes);
        dib.Draw([], scaling, 0, 0, 0, 0);
        dib.CopyPixels().Should().OnlyContain(value => value == 0);
    }

    [WindowsFact]
    public void ProductionDibReleaseIsIdempotentAndBoundsAreEnforced()
    {
        using var dib = new WindowsPresenceDib(3, 5);
        dib.Dispose();
        var read = () => dib.CopyPixels();
        read.Should().Throw<ObjectDisposedException>();
        var oversized = () =>
        {
            using var invalid = new WindowsPresenceDib(4097, 360);
        };
        oversized.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownMotionPreferenceFailsExplicitly(bool value)
    {
        var resolve = () => WindowsPresenceMotionPreference.Resolve(false, value, 5);
        resolve.Should().Throw<System.ComponentModel.Win32Exception>();
        WindowsPresenceMotionPreference.Resolve(true, value, 0).Should().Be(value);
    }
}