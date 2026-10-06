using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

[assembly: InternalsVisibleTo("Kora.Windows.IntegrationTests")]

namespace Kora.Setup;

public sealed class AnimatedMark : Control
{
    private static readonly Color[] Palette =
    [
        Color.Parse("#F49C9C"), Color.Parse("#F0CC83"), Color.Parse("#9BDFAC"),
        Color.Parse("#6AE1DA"), Color.Parse("#B8D9EC"), Color.Parse("#80B7FF"),
        Color.Parse("#AF9BFF"),
    ];
    private static readonly Point[] Ribbon = CreateRibbon();
    private static readonly double[] Distances = CreateDistances();
    private readonly RibbonSegment[] segments = CreateSegments();
    private AnimationLoop? animation;
    private bool animationEnabled;

    public bool AnimationEnabled
    {
        get => animationEnabled;
        set
        {
            if (animationEnabled == value)
            {
                return;
            }

            animationEnabled = value;
            UpdateAnimation();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var scale = Math.Min(Bounds.Width / 256, Bounds.Height / 320);
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale));
        for (var index = 1; index < Ribbon.Length; index++)
        {
            context.DrawLine(segments[index - 1].Pen, Ribbon[index - 1], Ribbon[index]);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } topLevel)
        {
            animation = new AnimationLoop(topLevel.RequestAnimationFrame, UpdateColours);
        }

        UpdateAnimation();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        animation?.Stop();
        animation = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateAnimation()
    {
        if (animationEnabled)
        {
            animation?.Start();
        }
        else
        {
            animation?.Stop();
        }
    }

    private void UpdateColours(double phase)
    {
        for (var index = 0; index < segments.Length; index++)
        {
            var position = Distances[index + 1] / Distances[^1];
            segments[index].Brush.Color = ColourAt(position - phase);
        }

        InvalidateVisual();
    }

    internal static Color ColourAt(double position)
    {
        var wrapped = (position - Math.Floor(position)) * Palette.Length;
        var index = (int)wrapped;
        var next = Palette[(index + 1) % Palette.Length];
        var current = Palette[index];
        var blend = wrapped - index;
        return Color.FromRgb(
            (byte)Math.Round(current.R + ((next.R - current.R) * blend)),
            (byte)Math.Round(current.G + ((next.G - current.G) * blend)),
            (byte)Math.Round(current.B + ((next.B - current.B) * blend)));
    }

    private static RibbonSegment[] CreateSegments()
    {
        var result = new RibbonSegment[Ribbon.Length - 1];
        for (var index = 0; index < result.Length; index++)
        {
            var brush = new SolidColorBrush(ColourAt(Distances[index + 1] / Distances[^1]));
            var pen = new Pen(brush, 27, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            result[index] = new RibbonSegment(brush, pen);
        }

        return result;
    }

    private static double[] CreateDistances()
    {
        var result = new double[Ribbon.Length];
        for (var index = 1; index < Ribbon.Length; index++)
        {
            var delta = Ribbon[index] - Ribbon[index - 1];
            result[index] = result[index - 1] + Math.Sqrt((delta.X * delta.X) + (delta.Y * delta.Y));
        }

        return result;
    }

    private static Point[] CreateRibbon()
    {
        // Cubic geometry is shared with Design/Branding/Kora.Mark.Primary.svg.
        Point[] controlPoints =
        [
            new(128, 28), new(78, 28), new(52, 49), new(52, 88),
            new(52, 119), new(79, 141), new(128, 160),
            new(177, 179), new(204, 201), new(204, 232),
            new(204, 271), new(178, 292), new(128, 292),
            new(78, 292), new(52, 271), new(52, 232),
            new(52, 201), new(79, 179), new(128, 160),
            new(177, 141), new(204, 119), new(204, 88),
            new(204, 49), new(178, 28), new(128, 28),
        ];
        var points = new List<Point> { controlPoints[0] };
        for (var curve = 0; curve < 8; curve++)
        {
            for (var step = 1; step <= 48; step++)
            {
                var t = step / 48.0;
                var u = 1 - t;
                var offset = curve * 3;
                var p0 = controlPoints[offset];
                var p1 = controlPoints[offset + 1];
                var p2 = controlPoints[offset + 2];
                var p3 = controlPoints[offset + 3];
                points.Add(new Point(
                    (u * u * u * p0.X) + (3 * u * u * t * p1.X) + (3 * u * t * t * p2.X) + (t * t * t * p3.X),
                    (u * u * u * p0.Y) + (3 * u * u * t * p1.Y) + (3 * u * t * t * p2.Y) + (t * t * t * p3.Y)));
            }
        }

        return [.. points];
    }

    private sealed record RibbonSegment(SolidColorBrush Brush, Pen Pen);

    internal sealed class AnimationLoop(Action<Action<TimeSpan>> requestFrame, Action<double> renderFrame)
    {
        private const double LoopSeconds = 8;
        private TimeSpan? startedAt;
        private long generation;

        public bool IsRunning { get; private set; }

        public void Start()
        {
            if (IsRunning)
            {
                return;
            }

            IsRunning = true;
            generation++;
            RequestFrame();
        }

        public void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;
            generation++;
            startedAt = null;
            renderFrame(0);
        }

        private void RequestFrame()
        {
            // Avalonia frame requests are one-shot and cannot be cancelled. A pending request must
            // neither retain a detached mark nor restart an obsolete animation after reattachment.
            var weak = new WeakReference<AnimationLoop>(this);
            var requestedGeneration = generation;
            requestFrame(timestamp =>
            {
                if (weak.TryGetTarget(out var loop))
                {
                    loop.OnFrame(requestedGeneration, timestamp);
                }
            });
        }

        private void OnFrame(long requestedGeneration, TimeSpan timestamp)
        {
            if (!IsRunning || requestedGeneration != generation)
            {
                return;
            }

            startedAt ??= timestamp;
            var elapsed = timestamp - startedAt.Value;
            renderFrame((elapsed.TotalSeconds % LoopSeconds) / LoopSeconds);
            if (IsRunning && requestedGeneration == generation)
            {
                RequestFrame();
            }
        }
    }
}
