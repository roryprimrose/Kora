using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.VisualTree;

using Kora.Application.Visuals;
using Kora.Core;
using Kora.Core.Configuration;

namespace Kora.Controls;

public sealed class PresenceControl : Control, IDisposable
{
    private const string ActualThemeVariantPropertyName = "ActualThemeVariant";

    public static readonly StyledProperty<AssistantState> StateProperty =
        AvaloniaProperty.Register<PresenceControl, AssistantState>(nameof(State), AssistantState.Information);

    public static readonly StyledProperty<bool> IsSpeakingProperty =
        AvaloniaProperty.Register<PresenceControl, bool>(nameof(IsSpeaking));

    public static readonly StyledProperty<bool> IsSpeechWorkActiveProperty =
        AvaloniaProperty.Register<PresenceControl, bool>(nameof(IsSpeechWorkActive));

    public static readonly StyledProperty<double> SpeechOutputLevelProperty =
        AvaloniaProperty.Register<PresenceControl, double>(nameof(SpeechOutputLevel));

    public static readonly StyledProperty<int> DotSizePercentProperty =
        AvaloniaProperty.Register<PresenceControl, int>(
            nameof(DotSizePercent), PresenceSettings.DefaultDotSizePercent);

    public static readonly StyledProperty<int> DotDensityPercentProperty =
        AvaloniaProperty.Register<PresenceControl, int>(
            nameof(DotDensityPercent), PresenceSettings.DefaultDotDensityPercent);

    public static readonly StyledProperty<bool> IsSpeechScalingEnabledProperty =
        AvaloniaProperty.Register<PresenceControl, bool>(
            nameof(IsSpeechScalingEnabled), PresenceSettings.DefaultSpeechScalingEnabled);

    public static readonly StyledProperty<int> SpeechScaleAmountPercentProperty =
        AvaloniaProperty.Register<PresenceControl, int>(
            nameof(SpeechScaleAmountPercent), PresenceSettings.DefaultSpeechScaleAmountPercent);

    public static readonly StyledProperty<int> MovementSpeedPercentProperty =
        AvaloniaProperty.Register<PresenceControl, int>(
            nameof(MovementSpeedPercent), PresenceSettings.DefaultMovementSpeedPercent);

    public static readonly StyledProperty<bool> IsPresenceVisibleProperty =
        AvaloniaProperty.Register<PresenceControl, bool>(nameof(IsPresenceVisible), true);

    public static readonly StyledProperty<bool> IsPresentationEligibleProperty =
        AvaloniaProperty.Register<PresenceControl, bool>(nameof(IsPresentationEligible));

    public static readonly StyledProperty<bool> IsMotionEnabledProperty =
        AvaloniaProperty.Register<PresenceControl, bool>(nameof(IsMotionEnabled), true);

    private PresenceAnimation animation = new(AssistantState.Hidden);
    private readonly Particle[] particles;
    private readonly int[] depthOrder;
    private readonly PresenceRenderLoop loop;
    private readonly List<Visual> ancestors = [];
    private readonly IBrush[] brushes = new IBrush[6];
    private Color? brushColor;
    private double brushOpacity = -1;
    private bool attached;
    private bool isAnimationEnabled = true;
    private bool disposed;
    private bool transitioning;
    private Func<bool>? authoritativeEligibility;
    private PresenceStateAutomationPeer? automationPeer;
    private (AssistantState State, bool Speaking, double Level, bool Scaling, int Amount, bool Visible) previousInputs =
        (AssistantState.Information, false, 0, true, PresenceSettings.DefaultSpeechScaleAmountPercent, true);

    public PresenceControl()
        : this(new DispatcherPresenceFrameScheduler())
    {
    }

    internal PresenceControl(IPresenceFrameScheduler scheduler)
    {
        var random = new Random(104729);
        particles = Enumerable.Range(0, PresenceSettings.GetParticleCount(PresenceSettings.MaximumDotDensityPercent))
            .Select(_ => new Particle(
                random.NextDouble() * 2 - 1,
                random.NextDouble() * 2 - 1,
                random.NextDouble() * 2 - 1,
                (random.NextDouble() - 0.5) * 0.012,
                (random.NextDouble() - 0.5) * 0.012,
                0.7 + random.NextDouble() * 1.2))
            .ToArray();

        // Z never moves. Filter this order by the original seeded prefix, not by sorted rank.
        depthOrder = Enumerable.Range(0, particles.Length).OrderBy(index => particles[index].Z).ToArray();
        loop = new PresenceRenderLoop(scheduler, CanRender, UpdateFrame, GetCadence);
    }

    internal long TickCount => loop.TickCount;
    internal long InvalidationCount { get; private set; }
    internal long ResourceBuildCount { get; private set; }
    internal int AncestorSubscriptionCount => ancestors.Count;
    internal ReadOnlySpan<int> DepthOrder => depthOrder;
    internal ReadOnlySpan<Particle> Particles => particles;
    internal PresenceVisualFrame CurrentFrame => animation.Current;
    internal ReadOnlySpan<IBrush> Brushes => brushes;
    internal bool CanInteract => CanRender() && IsPresenceVisible
        && State != AssistantState.Hidden && animation.Current.Opacity > 0;

    internal PresenceRenderInputs CaptureRenderInputs(bool isLightTheme, double width, double height, double scaling) =>
        new(State, IsSpeaking, SpeechOutputLevel, IsSpeechScalingEnabled, SpeechScaleAmountPercent,
            DotDensityPercent, DotSizePercent, MovementSpeedPercent, isLightTheme, !IsMotionEnabled,
            IsPresenceVisible, width, height, scaling);

    internal void BindPresentationEligibility(Func<bool> eligibility)
    {
        authoritativeEligibility = eligibility;
        automationPeer?.Refresh();
        loop.Wake();
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        automationPeer ??= new PresenceStateAutomationPeer(this, CaptureAccessibleState);

    private (AssistantState State, bool IsSpeaking)? CaptureAccessibleState() =>
        CanRender() && IsEffectivelyEnabled && IsPresenceVisible
            ? (State, IsSpeaking) : null;

    public bool IsPresentationEligible
    {
        get => GetValue(IsPresentationEligibleProperty);
        set => SetValue(IsPresentationEligibleProperty, value);
    }

    public bool IsMotionEnabled
    {
        get => GetValue(IsMotionEnabledProperty);
        set => SetValue(IsMotionEnabledProperty, value);
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

    public bool IsSpeechWorkActive
    {
        get => GetValue(IsSpeechWorkActiveProperty);
        set => SetValue(IsSpeechWorkActiveProperty, value);
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

    public int DotDensityPercent
    {
        get => GetValue(DotDensityPercentProperty);
        set => SetValue(DotDensityPercentProperty, value);
    }

    public bool IsSpeechScalingEnabled
    {
        get => GetValue(IsSpeechScalingEnabledProperty);
        set => SetValue(IsSpeechScalingEnabledProperty, value);
    }

    public int SpeechScaleAmountPercent
    {
        get => GetValue(SpeechScaleAmountPercentProperty);
        set => SetValue(SpeechScaleAmountPercentProperty, value);
    }

    public bool IsPresenceVisible
    {
        get => GetValue(IsPresenceVisibleProperty);
        set => SetValue(IsPresenceVisibleProperty, value);
    }

    public event EventHandler? FrameUpdating;

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var frame = animation.Current;
        if (!CanRender() || frame.Opacity <= 0)
        {
            return;
        }

        PrepareBrushes();
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var scale = Math.Min(Bounds.Width, Bounds.Height) * 0.36 * frame.Scale;

        var particleCount = PresenceSettings.GetParticleCount(DotDensityPercent);
        foreach (var index in depthOrder)
        {
            if (index >= particleCount)
            {
                continue;
            }
            var particle = particles[index];
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
        if (disposed)
        {
            return;
        }
        attached = true;
        foreach (var ancestor in this.GetVisualAncestors())
        {
            ancestors.Add(ancestor);
            ancestor.PropertyChanged += OnAncestorPropertyChanged;
        }
        automationPeer?.Refresh();
        loop.Wake();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        attached = false;
        automationPeer?.Refresh();
        loop.Stop();
        UnsubscribeAncestors();
        base.OnDetachedFromVisualTree(e);
    }

    internal void SetAnimationEnabled(bool enabled)
    {
        isAnimationEnabled = enabled;
        automationPeer?.Refresh();
        loop.Wake();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StateProperty || change.Property == IsSpeakingProperty
            || change.Property == IsPresenceVisibleProperty || change.Property == IsPresentationEligibleProperty
            || change.Property == IsVisibleProperty || change.Property == IsEnabledProperty
            || change.Property == OpacityProperty || change.Property == BoundsProperty)
        {
            automationPeer?.Refresh();
        }
        if (loop is not null && (change.Property == StateProperty
            || change.Property == IsSpeakingProperty
            || change.Property == IsSpeechWorkActiveProperty
            || change.Property == SpeechOutputLevelProperty
            || change.Property == DotSizePercentProperty
            || change.Property == DotDensityPercentProperty
            || change.Property == MovementSpeedPercentProperty
            || change.Property == IsSpeechScalingEnabledProperty
            || change.Property == SpeechScaleAmountPercentProperty
            || change.Property == IsPresenceVisibleProperty
            || change.Property == IsPresentationEligibleProperty
            || change.Property == IsMotionEnabledProperty
            || change.Property == IsVisibleProperty
            || change.Property == OpacityProperty
            || change.Property == BoundsProperty
            || string.Equals(
            change.Property.Name,
            ActualThemeVariantPropertyName,
            StringComparison.Ordinal)))
        {
            loop.Wake();
        }
    }

    public void Dispose()
    {
        disposed = true;
        automationPeer?.Refresh();
        loop.Dispose();
        UnsubscribeAncestors();
        authoritativeEligibility = null;
        FrameUpdating = null;
    }

    private void UnsubscribeAncestors()
    {
        foreach (var ancestor in ancestors)
        {
            ancestor.PropertyChanged -= OnAncestorPropertyChanged;
        }
        ancestors.Clear();
    }

    private void OnAncestorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == IsEnabledProperty)
        {
            automationPeer?.Refresh();
        }
        if (change.Property == IsVisibleProperty || change.Property == OpacityProperty
            || change.Property == BoundsProperty || change.Property == Window.WindowStateProperty)
        {
            automationPeer?.Refresh();
            loop.Wake();
        }
    }

    private bool CanRender() => !disposed && attached && isAnimationEnabled && IsEffectivelyVisible && Opacity > 0
        && IsPresentationEligible && (authoritativeEligibility?.Invoke() ?? true)
        && Bounds.Width > 0 && Bounds.Height > 0
        && (State != AssistantState.Hidden || animation.Current.Opacity > 0)
        && (IsPresenceVisible || animation.Current.Opacity > 0)
        && ancestors.TrueForAll(static ancestor => ancestor.IsVisible && ancestor.Opacity > 0
            && ancestor is not Window { WindowState: WindowState.Minimized });

    private TimeSpan? GetCadence()
    {
        if (IsSpeaking || IsSpeechWorkActive)
        {
            return PresenceRenderLoop.SpeechInterval;
        }
        if (transitioning || GetActivity() > 0)
        {
            return PresenceRenderLoop.ActiveInterval;
        }
        return null;
    }

    private void UpdateFrame(TimeSpan elapsed, bool resumed, bool isWake)
    {
        FrameUpdating?.Invoke(this, EventArgs.Empty);
        if (!CanRender())
        {
            return;
        }
        if (resumed)
        {
            animation = new PresenceAnimation(State);
            animation.Advance(AssistantState.Hidden, false, 0, PresenceAnimation.VisibilityTransitionDuration);
        }
        var inputs = (State, IsSpeaking, SpeechOutputLevel, IsSpeechScalingEnabled,
            SpeechScaleAmountPercent, IsPresenceVisible);
        var inputsChanged = inputs != previousInputs;
        var visualChanged = false;
        if (inputsChanged && !resumed)
        {
            // Time before an input edge belongs to the previous target, not the new transition.
            visualChanged = animation.Advance(previousInputs.State, previousInputs.Speaking, previousInputs.Level, elapsed,
                previousInputs.Scaling, previousInputs.Amount, previousInputs.Visible);
        }
        visualChanged |= animation.Advance(
            State,
            IsSpeaking,
            SpeechOutputLevel,
            inputsChanged ? TimeSpan.Zero : elapsed,
            IsSpeechScalingEnabled,
            SpeechScaleAmountPercent,
            IsPresenceVisible);
        previousInputs = inputs;
        transitioning = visualChanged || elapsed == TimeSpan.Zero || inputsChanged;
        var activity = GetActivity();
        var remaining = elapsed.TotalMilliseconds;
        while (activity > 0 && remaining > 0)
        {
            var step = Math.Min(remaining, PresenceRenderLoop.ActiveInterval.TotalMilliseconds);
            MoveParticles(activity * step / PresenceRenderLoop.ActiveInterval.TotalMilliseconds);
            remaining -= step;
        }
        if (isWake || inputsChanged || visualChanged || activity > 0 && elapsed > TimeSpan.Zero)
        {
            InvalidationCount++;
            InvalidateVisual();
        }
    }

    private double GetActivity()
    {
        return PresenceParticleField.GetActivity(State, MovementSpeedPercent, IsMotionEnabled);
    }

    private void MoveParticles(double activity)
    {
        var particleCount = PresenceSettings.GetParticleCount(DotDensityPercent);
        for (var index = 0; index < particleCount; index++)
        {
            var particle = particles[index];
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

    internal void PrepareBrushes()
    {
        var frame = animation.Current;
        var color = ResolveParticleColor(frame.Color.Red, frame.Color.Green, frame.Color.Blue);
        if (brushColor == color && brushOpacity == frame.Opacity)
        {
            return;
        }
        for (var index = 0; index < brushes.Length; index++)
        {
            brushes[index] = new ImmutableSolidColorBrush(Color.FromArgb(
                (byte)Math.Round((70 + (index * 28)) * frame.Opacity), color.R, color.G, color.B));
        }
        brushColor = color;
        brushOpacity = frame.Opacity;
        ResourceBuildCount++;
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

    internal sealed class Particle(
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