using Avalonia.Media;

using Kora.Application.Visuals;
using Kora.Core;
using Kora.Core.Configuration;

using SkiaSharp;

namespace Presence.Performance;

internal sealed class ParticleScene : IDisposable
{
    private readonly Particle[] particles;
    private readonly int[] order;
    private readonly SolidColorBrush[] brushes = Enumerable.Range(0, 6).Select(_ => new SolidColorBrush()).ToArray();
    private readonly SKPaint paint = new() { IsAntialias = true };
    private readonly SKImage sprite;
    private readonly SKRotationScaleMatrix[] transforms;
    private readonly SKRect[] spriteRects;
    private readonly SKColor[] spriteColors;
    private readonly PresenceAnimation animation = new(AssistantState.Information);
    private readonly float[]? trajectories;
    private double motionTime;
    private PresenceVisualFrame brushFrame;
    private bool brushLight;
    private bool brushesInitialized;

    public ParticleScene(int count, bool cacheTrajectories)
    {
        var random = new Random(104729);
        particles = Enumerable.Range(0, PresenceSettings.GetParticleCount(PresenceSettings.MaximumDotDensityPercent))
            .Select(_ => new Particle(
                random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1,
                (random.NextDouble() - 0.5) * 0.012, (random.NextDouble() - 0.5) * 0.012,
                0.7 + random.NextDouble() * 1.2))
            .ToArray();
        Count = count;
        transforms = new SKRotationScaleMatrix[count];
        spriteRects = Enumerable.Repeat(new SKRect(0, 0, 64, 64), count).ToArray();
        spriteColors = new SKColor[count];
        using (var surface = SKSurface.Create(new SKImageInfo(64, 64)))
        {
            surface.Canvas.Clear(SKColors.Transparent);
            paint.Color = SKColors.White;
            surface.Canvas.DrawCircle(32, 32, 30, paint);
            sprite = surface.Snapshot();
        }
        // Select the same seeded prefix as production, then sort that subset, not the whole population.
        order = Enumerable.Range(0, count).OrderBy(index => particles[index].Z).ToArray();
        if (cacheTrajectories)
        {
            trajectories = new float[600 * count * 2];
            for (var frame = 0; frame < 600; frame++)
            {
                for (var index = 0; index < count; index++)
                {
                    var particle = particles[index];
                    var position = ClosedPosition(particle.X, particle.Y, particle.Z, frame / 600d);
                    trajectories[(frame * count + index) * 2] = (float)position.X;
                    trajectories[(frame * count + index) * 2 + 1] = (float)position.Y;
                }
            }
        }
    }

    public int Count { get; }
    public PresenceVisualFrame Frame => animation.Current;
    public int CacheBytes => trajectories?.Length * sizeof(float) ?? 0;

    public static (double X, double Y) ClosedPosition(double x, double y, double z, double phase)
    {
        var angle = phase * Math.Tau;
        var frequency = 1 + (int)((z + 1) * 1.49);
        return (
            0.55 * Math.Sin(angle * frequency + x * Math.PI) + 0.25 * Math.Sin(angle * (frequency + 1) + z * Math.PI),
            0.55 * Math.Sin(angle * (frequency + 1) + y * Math.PI) + 0.25 * Math.Cos(angle * frequency + x * Math.PI));
    }

    public bool Advance(AssistantState state, bool speaking, double level, TimeSpan elapsed, int speed, bool timeBased)
    {
        var changed = animation.Advance(state, speaking, level, elapsed, true, 200);
        var activity = GetActivity(state) * speed / 100d;
        if (activity <= 0)
        {
            return changed;
        }
        motionTime += elapsed.TotalSeconds * activity;
        if (trajectories is null)
        {
            var step = timeBased ? Math.Clamp(elapsed.TotalMilliseconds / 33, 0, 3) : 1;
            for (var index = 0; index < Count; index++)
            {
                var particle = particles[index];
                particle.X += particle.Dx * activity * step;
                particle.Y += particle.Dy * activity * step;
                if (Math.Sqrt(particle.X * particle.X + particle.Y * particle.Y) > 1)
                {
                    particle.Dx -= particle.X * 0.0009 * activity * step;
                    particle.Dy -= particle.Y * 0.0009 * activity * step;
                }
                if (Math.Abs(particle.X) > 1.18)
                {
                    particle.Dx *= -0.85;
                }
                if (Math.Abs(particle.Y) > 1.18)
                {
                    particle.Dy *= -0.85;
                }
            }
        }
        return true;
    }

    public static double GetActivity(AssistantState state) => state switch
    {
        AssistantState.Listening => 0.8,
        AssistantState.Calculating => 1.25,
        AssistantState.Waiting => 0.12,
        AssistantState.Executing => 1.5,
        AssistantState.Success => 0.08,
        AssistantState.Failure => 0,
        AssistantState.Information => 0.28,
        _ => 0,
    };

    public (double X, double Y, double Radius, int Depth) GetDot(int ordinal, double size, double dotSize)
    {
        var index = order[ordinal];
        var particle = particles[index];
        var x = particle.X;
        var y = particle.Y;
        if (trajectories is not null)
        {
            var sample = motionTime / 20 * 600 % 600;
            var first = (int)sample;
            var second = (first + 1) % 600;
            var fraction = sample - first;
            var a = (first * Count + index) * 2;
            var b = (second * Count + index) * 2;
            x = trajectories[a] + (trajectories[b] - trajectories[a]) * fraction;
            y = trajectories[a + 1] + (trajectories[b + 1] - trajectories[a + 1]) * fraction;
        }
        var perspective = 0.72 + ((particle.Z + 1) * 0.18);
        var scale = size * 0.36 * Frame.Scale;
        return (size / 2 + x * scale * perspective, size / 2 + y * scale * perspective,
            particle.Size * dotSize / 100 * (0.7 + (particle.Z + 1) * 0.24),
            Math.Clamp((int)((particle.Z + 1) * 2.75), 0, 5));
    }

    public void Draw(Avalonia.Media.DrawingContext context, double size, double dotSize, bool light)
    {
        UpdateBrushes(light);
        for (var ordinal = 0; ordinal < Count; ordinal++)
        {
            var dot = GetDot(ordinal, size, dotSize);
            context.DrawEllipse(brushes[dot.Depth], null, new Avalonia.Point(dot.X, dot.Y), dot.Radius, dot.Radius);
        }
    }

    public void Draw(SKCanvas canvas, double size, double dotSize, bool light)
    {
        for (var ordinal = 0; ordinal < Count; ordinal++)
        {
            var dot = GetDot(ordinal, size, dotSize);
            paint.Color = GetColor(dot.Depth, light);
            canvas.DrawCircle((float)dot.X, (float)dot.Y, (float)dot.Radius, paint);
        }
    }

    public void DrawAtlas(SKCanvas canvas, double size, double dotSize, bool light)
    {
        for (var ordinal = 0; ordinal < Count; ordinal++)
        {
            var dot = GetDot(ordinal, size, dotSize);
            var scale = (float)dot.Radius / 30;
            transforms[ordinal] = new SKRotationScaleMatrix(scale, 0,
                (float)dot.X - 32 * scale, (float)dot.Y - 32 * scale);
            spriteColors[ordinal] = GetColor(dot.Depth, light);
        }
        canvas.DrawAtlas(sprite, spriteRects, transforms, spriteColors, SKBlendMode.Modulate,
            new SKSamplingOptions(SKFilterMode.Linear), null);
    }

    public SKColor GetColor(int depth, bool light)
    {
        var factor = light ? 0.55 : 1;
        return new SKColor(
            (byte)Math.Round(Frame.Color.Red * factor),
            (byte)Math.Round(Frame.Color.Green * factor),
            (byte)Math.Round(Frame.Color.Blue * factor),
            (byte)Math.Round((70 + depth * 28) * Frame.Opacity));
    }

    private void UpdateBrushes(bool light)
    {
        if (brushesInitialized && brushFrame.Color == Frame.Color && brushFrame.Opacity == Frame.Opacity && brushLight == light)
        {
            return;
        }
        for (var index = 0; index < brushes.Length; index++)
        {
            var color = GetColor(index, light);
            brushes[index].Color = Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);
        }
        brushFrame = Frame;
        brushLight = light;
        brushesInitialized = true;
    }

    public void Dispose()
    {
        paint.Dispose();
        sprite.Dispose();
    }

    internal sealed class Particle(double x, double y, double z, double dx, double dy, double size)
    {
        public double X { get; set; } = x;
        public double Y { get; set; } = y;
        public double Z { get; } = z;
        public double Dx { get; set; } = dx;
        public double Dy { get; set; } = dy;
        public double Size { get; } = size;
    }
}
