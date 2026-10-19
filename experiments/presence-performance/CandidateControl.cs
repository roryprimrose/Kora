using System.Diagnostics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;

using Kora.Core;

using SkiaSharp;

namespace Presence.Performance;

internal sealed class CandidateControl : Control, IDisposable
{
    private readonly string variant;
    private readonly ParticleScene scene;
    private readonly DispatcherTimer timer;
    private readonly double dotSize;
    private long previous;
    private AssistantState state;
    private bool speaking;
    private double level;
    private int speed;
    private bool light;
    private readonly SKImage[]? bitmapFrames;
    private readonly SKPaint imagePaint = new();
    private double bitmapPhase;

    public CandidateControl(string variant, int count, double dotSize)
    {
        this.variant = variant;
        this.dotSize = dotSize;
        scene = new ParticleScene(count, variant is "trajectory" or "trajectory15");
        if (variant is "bitmap")
        {
            var size = count == 300 ? 600 : 360;
            bitmapFrames = new SKImage[120];
            using var bakingScene = new ParticleScene(count, cacheTrajectories: true);
            using var surface = SKSurface.Create(new SKImageInfo(size, size));
            for (var index = 0; index < bitmapFrames.Length; index++)
            {
                surface.Canvas.Clear(SKColors.Transparent);
                bakingScene.Advance(AssistantState.Information, false, 0, TimeSpan.FromMilliseconds(33), 357, false);
                bakingScene.Draw(surface.Canvas, size, dotSize, false);
                bitmapFrames[index] = surface.Snapshot();
            }
        }
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(variant is "adaptive15" or "trajectory15" ? 66 : 33),
            DispatcherPriority.Background, OnTick);
    }

    public event EventHandler? FrameUpdating;
    public int CacheBytes => scene.CacheBytes
        + (bitmapFrames is null ? 0 : bitmapFrames.Length * (scene.Count == 300 ? 600 * 600 : 360 * 360) * 4);
    public long Draws;
    public long DrawTicks;
    public bool? GpuCanvas { get; private set; }

    public void SetInputs(AssistantState state, bool speaking, double level, int speed, bool light)
    {
        var changed = this.state != state || this.speaking != speaking || this.level != level
            || this.speed != speed || this.light != light;
        this.state = state;
        this.speaking = speaking;
        this.level = level;
        this.speed = speed;
        this.light = light;
        if (changed && !timer.IsEnabled)
        {
            previous = Stopwatch.GetTimestamp();
            timer.Start();
        }
    }

    public void Suspend() => timer.Stop();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        previous = Stopwatch.GetTimestamp();
        timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTick(object? sender, EventArgs args)
    {
        FrameUpdating?.Invoke(this, EventArgs.Empty);
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(previous, now);
        bitmapPhase += elapsed.TotalSeconds * ParticleScene.GetActivity(state) * speed / 100 * 30;
        var changed = scene.Advance(state, speaking, level, elapsed, speed,
            timeBased: variant is "adaptive15" or "trajectory15");
        previous = now;
        if (changed)
        {
            InvalidateVisual();
        }
        else
        {
            timer.Stop();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (variant is "cached" or "adaptive15")
        {
            var start = Stopwatch.GetTimestamp();
            scene.Draw(context, Bounds.Width, dotSize, light);
            Interlocked.Add(ref DrawTicks, Stopwatch.GetTimestamp() - start);
            Interlocked.Increment(ref Draws);
        }
        else
        {
            using var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(new SKRect(0, 0, (float)Bounds.Width, (float)Bounds.Height));
            if (variant is "atlas")
            {
                scene.DrawAtlas(canvas, Bounds.Width, dotSize, light);
            }
            else if (bitmapFrames is not null)
            {
                var color = scene.GetColor(5, light);
                using var tint = SKColorFilter.CreateBlendMode(
                    new SKColor(color.Red, color.Green, color.Blue), SKBlendMode.SrcIn);
                imagePaint.ColorFilter = tint;
                imagePaint.Color = SKColors.White.WithAlpha((byte)Math.Round(scene.Frame.Opacity * 255));
                var scale = (float)scene.Frame.Scale;
                canvas.Translate((float)Bounds.Width / 2, (float)Bounds.Height / 2);
                canvas.Scale(scale);
                canvas.Translate(-(float)Bounds.Width / 2, -(float)Bounds.Height / 2);
                canvas.DrawImage(bitmapFrames[(int)bitmapPhase % bitmapFrames.Length], 0, 0, imagePaint);
                imagePaint.ColorFilter = null;
            }
            else
            {
                scene.Draw(canvas, Bounds.Width, dotSize, light);
            }
            context.Custom(new PictureOperation(this, recorder.EndRecording(), Bounds));
        }
    }

    public void Dispose()
    {
        timer.Stop();
        scene.Dispose();
        imagePaint.Dispose();
        if (bitmapFrames is not null)
        {
            foreach (var image in bitmapFrames)
            {
                image.Dispose();
            }
        }
    }

    private sealed class PictureOperation(CandidateControl owner, SKPicture picture, Rect bounds) : ICustomDrawOperation
    {
        public Rect Bounds { get; } = bounds;
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>()
                ?? throw new InvalidOperationException("The candidate requires the Skia rendering backend.");
            using var lease = feature.Lease();
            owner.GpuCanvas = lease.GrContext is not null;
            var start = Stopwatch.GetTimestamp();
            lease.SkCanvas.DrawPicture(picture);
            Interlocked.Add(ref owner.DrawTicks, Stopwatch.GetTimestamp() - start);
            Interlocked.Increment(ref owner.Draws);
        }

        public void Dispose() => picture.Dispose();
    }
}
