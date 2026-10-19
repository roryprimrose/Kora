using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;

namespace Presence.Performance;

internal sealed class RenderProbe : Control
{
    public long RenderCount;
    public bool? GpuCanvas { get; private set; }
    public bool ObservedGpu { get; private set; }
    public bool ObservedSoftware { get; private set; }

    public override void Render(DrawingContext context) =>
        context.Custom(new ProbeOperation(this, new Rect(Bounds.Size)));

    private sealed class ProbeOperation(RenderProbe owner, Rect bounds) : ICustomDrawOperation
    {
        public Rect Bounds { get; } = bounds;
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>()
                ?? throw new InvalidOperationException("A Skia draw probe is required.");
            using var lease = feature.Lease();
            owner.GpuCanvas = lease.GrContext is not null;
            owner.ObservedGpu |= owner.GpuCanvas == true;
            owner.ObservedSoftware |= owner.GpuCanvas == false;
            Interlocked.Increment(ref owner.RenderCount);
        }
    }
}
