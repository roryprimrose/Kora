using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using Kora.Windows.Presentation;

namespace Kora.Controls;

internal sealed class AvaloniaPresencePresenter : IDisposable
{
    private readonly Window owner;
    private readonly PresenceControl source;
    private readonly Func<bool> admitted;
    private readonly Action<Exception> reportFailure;
    private readonly IBitmapTarget target;
    private RenderTargetBitmap? bitmap;
    private bool drawing;
    private bool failed;
    private bool disposed;

    internal AvaloniaPresencePresenter(Window owner, PresenceControl source, Func<bool> admitted,
        Action<Exception> reportFailure, Func<nint, IBitmapTarget>? createTarget = null)
    {
        this.owner = owner;
        this.source = source;
        this.admitted = admitted;
        this.reportFailure = reportFailure;
        var handle = owner.TryGetPlatformHandle()?.Handle
            ?? throw new InvalidOperationException("The standard presence requires its host-owned window.");
        target = (createTarget ?? CreateNativeTarget)(handle);
        source.UseExternalPresentation();
        source.FrameReady += OnFrameReady;
        source.PropertyChanged += OnSourceChanged;
        source.DetachedFromVisualTree += OnDetached;
        owner.PropertyChanged += OnOwnerChanged;
        owner.PositionChanged += OnPositionChanged;
        owner.ScalingChanged += OnScalingChanged;
    }

    internal static IBitmapTarget CreateNativeTarget(nint parent) => new NativeBitmapTarget(new(parent));

    internal nint Handle => target.Handle;
    internal double PresentedOpacity { get; private set; }
    internal bool CanInteract => PresentedOpacity > 0 && CanPresent && source.CanInteract;
    private bool CanPresent => !disposed && !failed && admitted() && owner.IsVisible
        && owner.WindowState != WindowState.Minimized && owner.Opacity > 0 && source.HasVisibleFrame;

    private void OnFrameReady(object? sender, EventArgs args) => Refresh();
    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs args) => Suspend();
    private void OnPositionChanged(object? sender, PixelPointEventArgs args) => Refresh();
    private void OnScalingChanged(object? sender, EventArgs args) => Refresh();

    private void OnSourceChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Visual.IsVisibleProperty || args.Property == Visual.OpacityProperty
            || args.Property == Visual.BoundsProperty || args.Property == PresenceControl.IsPresentationEligibleProperty)
        {
            Refresh();
        }
    }

    private void OnOwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Visual.IsVisibleProperty || args.Property == Visual.OpacityProperty
            || args.Property == Window.WindowStateProperty)
        {
            Refresh();
        }
    }

    internal void Refresh()
    {
        if (disposed || failed) { return; }
        if (drawing) { return; }
        drawing = true;
        try
        {
            if (!CanPresent) { Suspend(); return; }
            var scaling = owner.RenderScaling;
            var frame = source.CurrentFrame;
            var ownerOpacity = owner.Opacity;
            var sourceOpacity = source.Opacity;
            var size = new PixelSize(checked((int)Math.Ceiling(source.Bounds.Width * scaling)),
                checked((int)Math.Ceiling(source.Bounds.Height * scaling)));
            if (size.Width is <= 0 or > 4096 || size.Height is <= 0 or > 4096)
            {
                throw new InvalidOperationException("The standard presence frame exceeds its physical pixel bounds.");
            }
            var dpi = new Vector(96 * scaling, 96 * scaling);
            if (bitmap is null || bitmap.PixelSize != size || bitmap.Dpi != dpi)
            {
                bitmap?.Dispose();
                bitmap = null;
                bitmap = new RenderTargetBitmap(size, dpi);
            }
            if (bitmap.Format != PixelFormats.Bgra8888 || bitmap.AlphaFormat != AlphaFormat.Premul)
            {
                throw new InvalidOperationException("The standard presence requires premultiplied BGRA pixels.");
            }
            using (var context = bitmap.CreateDrawingContext())
            using (context.PushOpacity(ownerOpacity * sourceOpacity))
            {
                source.DrawFrame(context);
            }
            var completed = bitmap;
            target.CopyFrame(size.Width, size.Height,
                (pixels, length, stride) => completed.CopyPixels(new PixelRect(size), pixels, length, stride));
            if (!CanPresent || owner.RenderScaling != scaling || source.CurrentFrame != frame
                || owner.Opacity != ownerOpacity || source.Opacity != sourceOpacity
                || Math.Ceiling(source.Bounds.Width * scaling) != size.Width
                || Math.Ceiling(source.Bounds.Height * scaling) != size.Height)
            {
                Suspend();
                return;
            }
            var origin = source.TranslatePoint(default, owner)
                ?? throw new InvalidOperationException("The standard presence has no host-relative position.");
            target.Move(checked((int)Math.Round(origin.X * scaling)), checked((int)Math.Round(origin.Y * scaling)));
            if (!CanPresent) { Suspend(); return; }
            target.Present();
            PresentedOpacity = frame.Opacity;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failed = true;
            Suspend();
            reportFailure(exception);
        }
        finally { drawing = false; }
    }

    internal void Suspend()
    {
        PresentedOpacity = 0;
        if (!disposed) { target.Hide(); }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        Suspend();
        disposed = true;
        source.FrameReady -= OnFrameReady;
        source.PropertyChanged -= OnSourceChanged;
        source.DetachedFromVisualTree -= OnDetached;
        owner.PropertyChanged -= OnOwnerChanged;
        owner.PositionChanged -= OnPositionChanged;
        owner.ScalingChanged -= OnScalingChanged;
        try { bitmap?.Dispose(); }
        finally
        {
            try { target.ReleaseDrawingResources(); }
            finally { target.Dispose(); }
        }
    }

    /// <summary>A UI-owned pixel buffer and compatible popup, independent of the Avalonia window class.</summary>
    internal interface IBitmapTarget : IDisposable
    {
        nint Handle { get; }
        void Move(int x, int y);
        void CopyFrame(int width, int height, Action<nint, int, int> copyPixels);
        void Present();
        void Hide();
        void ReleaseDrawingResources();
    }

    private sealed class NativeBitmapTarget(WindowsPresenceRenderTarget target) : IBitmapTarget
    {
        public nint Handle => target.Handle;
        public void Move(int x, int y) => target.Move(x, y);
        public void CopyFrame(int width, int height, Action<nint, int, int> copyPixels) =>
            target.CopyFrame(width, height, copyPixels);
        public void Present() => target.Present();
        public void Hide() => target.Hide();
        public void ReleaseDrawingResources() => target.ReleaseDrawingResources();
        public void Dispose() => target.Dispose();
    }
}
