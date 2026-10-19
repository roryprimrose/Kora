using System.Diagnostics;
using System.Numerics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;

using Kora.Core;

namespace Presence.Performance;

internal sealed class CompositionControl : Control, IDisposable
{
    private readonly Handler handler;
    private CompositionCustomVisual? visual;
    private Inputs inputs = new(AssistantState.Calculating, false, 0, 100, false, false);

    public CompositionControl(int count, double dotSize, bool atlas)
    {
        handler = new Handler(count, dotSize, atlas);
    }

    public long Updates => Interlocked.Read(ref handler.Updates);
    public long Draws => Interlocked.Read(ref handler.Draws);
    public bool? GpuCanvas => handler.GpuCanvas;

    public void SetInputs(AssistantState state, bool speaking, double level, int speed, bool light)
    {
        inputs = new Inputs(state, speaking, level, speed, light, false);
        visual?.SendHandlerMessage(inputs);
    }

    public void Suspend() => visual?.SendHandlerMessage(inputs with { Paused = true });

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var element = ElementComposition.GetElementVisual(this)
            ?? throw new InvalidOperationException("The attached control has no compositor.");
        visual = element.Compositor.CreateCustomVisual(handler);
        visual.Size = new Vector2((float)Bounds.Width, (float)Bounds.Height);
        ElementComposition.SetElementChildVisual(this, visual);
        visual.SendHandlerMessage(inputs);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty && visual is not null)
        {
            visual.Size = new Vector2((float)Bounds.Width, (float)Bounds.Height);
        }
    }

    public void Dispose()
    {
        if (visual is not null)
        {
            visual.SendHandlerMessage("dispose");
            ElementComposition.SetElementChildVisual(this, null);
        }
    }

    private sealed record Inputs(AssistantState State, bool Speaking, double Level, int Speed, bool Light, bool Paused);

    private sealed class Handler(int count, double dotSize, bool atlas) : CompositionCustomVisualHandler, IDisposable
    {
        private readonly ParticleScene scene = new(count, cacheTrajectories: false);
        private Inputs inputs = new(AssistantState.Calculating, false, 0, 100, false, false);
        private TimeSpan previous;
        private TimeSpan nextFrame;
        private bool scheduled;
        private bool disposed;
        public long Updates;
        public long Draws;
        public bool? GpuCanvas { get; private set; }

        public override void OnMessage(object message)
        {
            if (message is Inputs next)
            {
                inputs = next;
                if (!inputs.Paused && !scheduled && !disposed)
                {
                    previous = CompositionNow;
                    nextFrame = previous + TimeSpan.FromMilliseconds(33);
                    scheduled = true;
                    RegisterForNextAnimationFrameUpdate();
                }
            }
            else if (message is "dispose")
            {
                Dispose();
            }
            else
            {
                throw new ArgumentException("Unknown compositor benchmark message.", nameof(message));
            }
        }

        public override void OnAnimationFrameUpdate()
        {
            scheduled = false;
            if (inputs.Paused || disposed)
            {
                return;
            }
            var elapsed = CompositionNow - previous;
            if (CompositionNow >= nextFrame)
            {
                nextFrame += TimeSpan.FromMilliseconds(33);
                if (CompositionNow - nextFrame > TimeSpan.FromMilliseconds(33))
                {
                    nextFrame = CompositionNow + TimeSpan.FromMilliseconds(33);
                }
                previous = CompositionNow;
                Interlocked.Increment(ref Updates);
                if (scene.Advance(inputs.State, inputs.Speaking, inputs.Level, elapsed, inputs.Speed, timeBased: true))
                {
                    Invalidate();
                }
                else
                {
                    return;
                }
            }
            scheduled = true;
            RegisterForNextAnimationFrameUpdate();
        }

        public override void OnRender(ImmediateDrawingContext context)
        {
            if (disposed)
            {
                return;
            }
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>()
                ?? throw new InvalidOperationException("The compositor candidate requires Skia.");
            using var lease = feature.Lease();
            GpuCanvas = lease.GrContext is not null;
            if (atlas)
            {
                scene.DrawAtlas(lease.SkCanvas, EffectiveSize.X, dotSize, inputs.Light);
            }
            else
            {
                scene.Draw(lease.SkCanvas, EffectiveSize.X, dotSize, inputs.Light);
            }
            Interlocked.Increment(ref Draws);
        }

        public void Dispose()
        {
            disposed = true;
            scene.Dispose();
        }
    }
}
