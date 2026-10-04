using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

using Kora.Application.Visuals;
using Kora.Core;

namespace Kora.Controls;

public sealed class PresenceControl : Control
{
    private const string ActualThemeVariantPropertyName = "ActualThemeVariant";

    public static readonly StyledProperty<AssistantState> StateProperty =
        AvaloniaProperty.Register<PresenceControl, AssistantState>(nameof(State), AssistantState.Information);

    public static readonly StyledProperty<bool> IsSpeakingProperty =
        AvaloniaProperty.Register<PresenceControl, bool>(nameof(IsSpeaking));

    public static readonly StyledProperty<double> SpeechOutputLevelProperty =
        AvaloniaProperty.Register<PresenceControl, double>(nameof(SpeechOutputLevel));

    public static readonly StyledProperty<int> DotSizePercentProperty =
        AvaloniaProperty.Register<PresenceControl, int>(nameof(DotSizePercent), 100);

    public static readonly StyledProperty<int> MovementSpeedPercentProperty =
        AvaloniaProperty.Register<PresenceControl, int>(nameof(MovementSpeedPercent), 100);

    private readonly PresenceAnimation animation = new(AssistantState.Information);
    private readonly Particle[] particles;
    private readonly DispatcherTimer timer;

    static PresenceControl()
    {
        AffectsRender<PresenceControl>(
            StateProperty,
            IsSpeakingProperty,
            SpeechOutputLevelProperty,
            DotSizePercentProperty);
    }

    public PresenceControl()
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

        timer = new DispatcherTimer(PresenceAnimation.FrameInterval, DispatcherPriority.Background, OnTick);
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

    public int DotSizePercent
    {
        get => GetValue(DotSizePercentProperty);
        set => SetValue(DotSizePercentProperty, value);
    }

    public int MovementSpeedPercent
    {
        get => GetValue(MovementSpeedPercentProperty);
        set => SetValue(MovementSpeedPercentProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var frame = animation.Current;
        if (frame.Opacity <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var color = ResolveParticleColor(frame.Color.Red, frame.Color.Green, frame.Color.Blue);
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
            var radius = particle.Size
                         * (DotSizePercent / 100d)
                         * (0.7 + ((particle.Z + 1) * 0.24));
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (string.Equals(
            change.Property.Name,
            ActualThemeVariantPropertyName,
            StringComparison.Ordinal))
        {
            InvalidateVisual();
        }
    }

    private void OnTick(object? sender, EventArgs eventArgs)
    {
        var visualChanged = animation.Advance(
            State,
            IsSpeaking,
            SpeechOutputLevel,
            PresenceAnimation.FrameInterval);
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
        activity *= MovementSpeedPercent / 100d;

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

    private Color ResolveParticleColor(byte red, byte green, byte blue)
    {
        const double lightThemeFactor = 0.55;
        var factor = ActualThemeVariant == ThemeVariant.Light
            ? lightThemeFactor
            : 1;
        return Color.FromRgb(
            (byte)Math.Round(red * factor),
            (byte)Math.Round(green * factor),
            (byte)Math.Round(blue * factor));
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