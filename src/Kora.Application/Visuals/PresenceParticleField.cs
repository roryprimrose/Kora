using Kora.Core;
using Kora.Core.Configuration;
using Kora.Core.Presentation;

namespace Kora.Application.Visuals;

public sealed class PresenceParticleField
{
    public static readonly TimeSpan QuietFrameInterval = TimeSpan.FromMilliseconds(198);
    private readonly Particle[] particles;
    private readonly int[] depthOrder;

    public PresenceParticleField()
    {
        var random = new Random(104729);
        particles = Enumerable.Range(0, PresenceSettings.GetParticleCount(PresenceSettings.MaximumDotDensityPercent))
            .Select(_ => new Particle(random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1,
                random.NextDouble() * 2 - 1, (random.NextDouble() - 0.5) * 0.012,
                (random.NextDouble() - 0.5) * 0.012, 0.7 + random.NextDouble() * 1.2))
            .ToArray();
        depthOrder = Enumerable.Range(0, particles.Length).OrderBy(index => particles[index].Z).ToArray();
    }

    public bool Advance(AssistantState state, TimeSpan elapsed, int density, int speed, bool reducedMotion)
    {
        PresenceSettings.ValidateDotDensityPercent(density);
        PresenceSettings.ValidateMovementSpeedPercent(speed);
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        var activity = GetActivity(state, speed, !reducedMotion);
        var remaining = Math.Min(elapsed.TotalMilliseconds, QuietFrameInterval.TotalMilliseconds);
        var count = PresenceSettings.GetParticleCount(density);
        while (activity > 0 && remaining > 0)
        {
            var step = Math.Min(remaining, PresenceAnimation.FrameInterval.TotalMilliseconds);
            Move(count, activity * step / PresenceAnimation.FrameInterval.TotalMilliseconds);
            remaining -= step;
        }
        return activity > 0 && elapsed > TimeSpan.Zero;
    }

    public static double GetActivity(AssistantState state, int speed, bool motionEnabled)
    {
        PresenceSettings.ValidateMovementSpeedPercent(speed);
        var activity = !motionEnabled ? 0 : state switch
        {
            AssistantState.Listening => 0.8,
            AssistantState.Calculating => 1.25,
            AssistantState.Waiting => 0.35,
            AssistantState.Executing => 1.5,
            AssistantState.Success => 0.08,
            AssistantState.Information => 0.28,
            _ => 0,
        };
        return activity * speed / 100d;
    }

    private void Move(int count, double activity)
    {
        for (var index = 0; index < count; index++)
        {
            var particle = particles[index];
            particle.X += particle.Dx * activity;
            particle.Y += particle.Dy * activity;
            if (particle.X * particle.X + particle.Y * particle.Y > 1)
            {
                particle.Dx -= particle.X * 0.0009 * activity;
                particle.Dy -= particle.Y * 0.0009 * activity;
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

    public int Project(Span<PresenceParticle> destination, int density, int dotSize, double width, double height, double speechScale)
    {
        PresenceSettings.ValidateDotDensityPercent(density);
        PresenceSettings.ValidateDotSizePercent(dotSize);
        var count = PresenceSettings.GetParticleCount(density);
        if (destination.Length < count)
        {
            throw new ArgumentException("The destination cannot hold the selected particles.", nameof(destination));
        }
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(height) || height <= 0
            || !double.IsFinite(speechScale) || speechScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Projection dimensions and scale must be positive and finite.");
        }
        var scale = Math.Min(width, height) * 0.36 * speechScale;
        var output = 0;
        foreach (var index in depthOrder)
        {
            // Density selects the seeded prefix, never the nearest/dimmest subset of a depth-sorted population.
            if (index >= count)
            {
                continue;
            }
            var particle = particles[index];
            var perspective = 0.72 + (particle.Z + 1) * 0.18;
            destination[output++] = new PresenceParticle(
                width / 2 + particle.X * scale * perspective, height / 2 + particle.Y * scale * perspective,
                particle.Size * dotSize / 100d * (0.7 + (particle.Z + 1) * 0.24),
                Math.Clamp((int)((particle.Z + 1) * 2.75), 0, 5));
        }
        return output;
    }

    private sealed class Particle(double x, double y, double z, double dx, double dy, double size)
    {
        public double X = x;
        public double Y = y;
        public readonly double Z = z;
        public double Dx = dx;
        public double Dy = dy;
        public readonly double Size = size;
    }
}