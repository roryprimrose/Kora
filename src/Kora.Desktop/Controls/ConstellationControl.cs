using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

using Kora.Application.Visuals;
using Kora.Core;

namespace Kora.Desktop.Controls;

public sealed class ConstellationControl : Control
{
    public static readonly StyledProperty<AssistantState> StateProperty =
        AvaloniaProperty.Register<ConstellationControl, AssistantState>(nameof(State), AssistantState.Information);

    public static readonly StyledProperty<bool> IsSpeakingProperty =
        AvaloniaProperty.Register<ConstellationControl, bool>(nameof(IsSpeaking));

    public static readonly StyledProperty<double> SpeechOutputLevelProperty =
        AvaloniaProperty.Register<ConstellationControl, double>(nameof(SpeechOutputLevel));

    private readonly ConstellationAnimation animation = new(AssistantState.Information);
    private readonly Particle[] particles;
    private readonly DispatcherTimer timer;

    static ConstellationControl()
    {
        AffectsRender<ConstellationControl>(StateProperty, IsSpeakingProperty, SpeechOutputLevelProperty);
    }

    public ConstellationControl()
    {
        var random = new Random(104729);
        particles = Enumerable.Range(0, 150)
            .Select(_ => new Particle(
                random.NextDouble() * 2 - 1,
                random.NextDouble() * 2 - 1,
                random.NextDouble() * 2 - 1,
                (random.NextDouble() - 0.5) * 0.012,
                (random.NextDouble() - 0.5) * 0.012,
                0.7 + random.NextDouble() * 1.2))
            .ToArray();

        timer = new DispatcherTimer(ConstellationAnimation.FrameInterval, DispatcherPriority.Background, OnTick);
    }

    public AssistantState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public bool IsSpeaking
    {
        get => GetValue(IsSpeakingProperty);
        set => SetValue(IsSpeakingProperty, value);
    }

    public double SpeechOutputLevel
    {
        get => GetValue(SpeechOutputLevelProperty);
        set => SetValue(SpeechOutputLevelProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var frame = animation.Current;
        if (frame.Opacity <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var color = Color.FromRgb(frame.Color.Red, frame.Color.Green, frame.Color.Blue);
        var brushes = Enumerable.Range(0, 6)
            .Select(index => new SolidColorBrush(Color.FromArgb(
                (byte)Math.Round((70 + (index * 28)) * frame.Opacity),
                color.R,
                color.G,
                color.B)))
            .ToArray();
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var scale = Math.Min(Bounds.Width, Bounds.Height) * 0.36 * frame.Scale;

        foreach (var particle in particles.OrderBy(item => item.Z))
        {
            var perspective = 0.72 + ((particle.Z + 1) * 0.18);
            var x = center.X + particle.X * scale * perspective;
            var y = center.Y + particle.Y * scale * perspective;
            var depth = Math.Clamp((int)((particle.Z + 1) * 2.75), 0, brushes.Length - 1);
            var radius = particle.Size * (0.7 + ((particle.Z + 1) * 0.24));
            context.DrawEllipse(brushes[depth], null, new Point(x, y), radius, radius);
        }
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        timer.Start();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTick(object? sender, EventArgs eventArgs)
    {
        var visualChanged = animation.Advance(
            State,
            IsSpeaking,
            SpeechOutputLevel,
            ConstellationAnimation.FrameInterval);
        var activity = State switch
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

        if (activity > 0)
        {
            foreach (var particle in particles)
            {
                particle.X += particle.Dx * activity;
                particle.Y += particle.Dy * activity;

                var distance = Math.Sqrt((particle.X * particle.X) + (particle.Y * particle.Y));
                if (distance > 1)
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

        if (visualChanged || activity > 0)
        {
            InvalidateVisual();
        }
    }

    private sealed class Particle(
        double x,
        double y,
        double z,
        double dx,
        double dy,
        double size)
    {
        public double X { get; set; } = x;

        public double Y { get; set; } = y;

        public double Z { get; } = z;

        public double Dx { get; set; } = dx;

        public double Dy { get; set; } = dy;

        public double Size { get; } = size;
    }
}