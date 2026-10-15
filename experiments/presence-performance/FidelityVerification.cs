using System.Reflection;
using System.Text.Json;

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

using Kora.Controls;
using Kora.Core;
using Kora.Core.Configuration;

using SkiaSharp;

namespace Presence.Performance;

internal static class FidelityVerification
{
    public static void Run(string output)
    {
        var rows = new List<Comparison>();
        var tick = typeof(PresenceControl).GetMethod("OnTick", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException("The linked baseline tick method changed.");
        foreach (var density in new[] { 25, 100, 200 })
        {
            foreach (var size in new[] { 240, 360, 600 })
            {
                foreach (var dotSize in new[] { 50, 100, 200 })
                {
                    var baseline = new PresenceControl { DotDensityPercent = density, DotSizePercent = dotSize };
                    baseline.Measure(new Size(size, size));
                    baseline.Arrange(new Rect(0, 0, size, size));
                    using var scene = new ParticleScene(PresenceSettings.GetParticleCount(density), cacheTrajectories: false);
                    using var dib = new NativeDibBuffer(size, size);
                    for (var frame = 0; frame < 2; frame++)
                    {
                        if (frame != 0)
                        {
                            for (var step = 0; step < 60; step++)
                            {
                                _ = tick.Invoke(baseline, [null, EventArgs.Empty]);
                                _ = scene.Advance(AssistantState.Information, false, 0, TimeSpan.FromMilliseconds(33), 100, false);
                            }
                        }
                        var light = baseline.ActualThemeVariant == ThemeVariant.Light;
                        using var reference = DrawAvalonia(size, baseline.Render);
                        using var cached = DrawAvalonia(size, context => scene.Draw(context, size, dotSize, light));
                        using var direct = DrawSkia(size, canvas => scene.Draw(canvas, size, dotSize, light));
                        using var atlas = DrawSkia(size, canvas => scene.DrawAtlas(canvas, size, dotSize, light));
                        var cachedRow = Compare("cached", density, size, dotSize, frame * 60, reference, cached);
                        var directRow = Compare("skia-raster", density, size, dotSize, frame * 60, reference, direct);
                        dib.Draw(scene, dotSize, light);
                        var dibRow = CompareDib("native-dib-bytes", density, size, dotSize, frame * 60, reference, dib);
                        var dibSkiaRow = CompareDib("native-dib-vs-skia-bytes", density, size, dotSize, frame * 60, direct, dib);
                        rows.Add(cachedRow);
                        rows.Add(directRow);
                        rows.Add(dibRow);
                        rows.Add(dibSkiaRow);
                        rows.Add(Compare("atlas", density, size, dotSize, frame * 60, reference, atlas));
                        if (cachedRow.AlphaRelativeError > 0.000001 || directRow.AlphaRelativeError > 0.01
                            || dibRow.AlphaRelativeError > 0.01 || dibRow.DibBytes!.ChannelRelativeError > 0.01
                            || dibRow.DibBytes.InvalidPremultipliedPixels != 0 || dibSkiaRow.DibBytes!.MaximumChannelError != 0)
                        {
                            throw new InvalidOperationException($"Raster/DIB fidelity failed: {JsonSerializer.Serialize(new[] { cachedRow, directRow, dibRow, dibSkiaRow })}");
                        }
                    }
                    var pr = new Pr247.PresenceControl { DotDensityPercent = density, DotSizePercent = dotSize };
                    pr.Measure(new Size(size, size));
                    pr.Arrange(new Rect(0, 0, size, size));
                    var original = new PresenceControl { DotDensityPercent = density, DotSizePercent = dotSize };
                    original.Measure(new Size(size, size));
                    original.Arrange(new Rect(0, 0, size, size));
                    using var originalImage = DrawAvalonia(size, original.Render);
                    using var prImage = DrawAvalonia(size, pr.Render);
                    rows.Add(Compare("pr247-prefix", density, size, dotSize, 0, originalImage, prImage));
                }
            }
        }
        File.WriteAllText(output, JsonSerializer.Serialize(rows, Program.BenchmarkResult.JsonOptions));
        Console.WriteLine($"Fidelity: {rows.Count} image comparisons passed required cached/Skia/actual-DIB-byte checks. "
            + $"Atlas maximum alpha-relative error: {rows.Where(row => row.Variant is "atlas").Max(row => row.AlphaRelativeError):P2}.");
    }

    private static SKBitmap DrawAvalonia(int size, Action<DrawingContext> draw)
    {
        using var target = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var context = target.CreateDrawingContext())
        {
            draw(context);
        }
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static SKBitmap DrawSkia(int size, Action<SKCanvas> draw)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        draw(surface.Canvas);
        using var image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }

    private static Comparison CompareDib(string variant, int density, int size, int dotSize, int frame, SKBitmap reference, NativeDibBuffer dib)
    {
        var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var expected = new SKBitmap(info);
        using var referenceImage = SKImage.FromBitmap(reference);
        if (!referenceImage.ReadPixels(info, expected.GetPixels(), dib.Stride, 0, 0))
        {
            throw new InvalidOperationException("Could not convert the production raster to premultiplied BGRA.");
        }
        var comparison = BgraPixelComparison.Compare(expected.Bytes, dib.CopyBgraPixels());
        return new Comparison(variant, density, size, dotSize, frame,
            comparison.ChangedPixels, comparison.MeanChannelAbsoluteError, comparison.AlphaRelativeError, comparison);
    }

    private static Comparison Compare(string variant, int density, int size, int dotSize, int frame, SKBitmap reference, SKBitmap candidate)
    {
        long referenceAlpha = 0;
        long alphaError = 0;
        long allError = 0;
        var changed = 0;
        var expected = reference.Pixels;
        var actual = candidate.Pixels;
        for (var index = 0; index < expected.Length; index++)
        {
            var a = expected[index];
            var b = actual[index];
            referenceAlpha += a.Alpha;
            alphaError += Math.Abs(a.Alpha - b.Alpha);
            allError += Math.Abs(a.Alpha - b.Alpha) + Math.Abs(a.Red - b.Red)
                + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue);
            if (a != b)
            {
                changed++;
            }
        }
        if (referenceAlpha == 0)
        {
            throw new InvalidOperationException("Fidelity reference unexpectedly contains no visible pixels.");
        }
        return new Comparison(variant, density, size, dotSize, frame, changed,
            allError / (double)(expected.Length * 4), alphaError / (double)referenceAlpha);
    }

    private sealed record Comparison(string Variant, int Density, int Size, int DotSize, int Frame,
        int ChangedPixels, double MeanChannelAbsoluteError, double AlphaRelativeError, BgraPixelComparison.Result? DibBytes = null);
}
