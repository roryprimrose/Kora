using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Kora.Application.Visuals;
using Kora.Core;
using Kora.Windows.Presentation;

using Microsoft.Extensions.Logging;

namespace Kora.Controls;

internal sealed class NativePresenceHost(
    Func<PresenceRenderInputs?> capture,
    Action samplePlayback,
    Action<Exception> reportFailure,
    ILogger<PresenceRenderWorker> logger) : Control, IDisposable
{
    private readonly DispatcherTimer playbackTimer = new();
    private readonly List<Visual> ancestors = [];
    private TopLevel? topLevel;
    private WindowsPresenceRenderTarget? target;
    private PresenceRenderWorker? worker;
    private long attachmentGeneration;
    private long releasedFrames;
    private bool failed;
    private bool disposed;
    private bool positionUpdatePending;
    internal long FramesPresented => releasedFrames + (target?.FramesPresented ?? 0);
    internal nint NativeHandle => target?.Handle ?? 0;
    internal bool CanInteract => CanPresent && worker?.HasVisibleFrame == true
        && capture() is { IsPresenceVisible: true, State: not AssistantState.Hidden };
    private bool CanPresent => !disposed && !failed && topLevel is not null && IsEffectivelyVisible
        && Opacity > 0 && Bounds.Width > 0 && Bounds.Height > 0
        && ancestors.TrueForAll(static ancestor => ancestor.IsVisible && ancestor.Opacity > 0
            && ancestor is not Window { WindowState: WindowState.Minimized });

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ObjectDisposedException.ThrowIf(disposed, this);
        topLevel = TopLevel.GetTopLevel(this)
            ?? throw new InvalidOperationException("The native presence requires its host-owned top level.");
        var parent = topLevel.TryGetPlatformHandle()
            ?? throw new InvalidOperationException("The native presence host has no Windows handle.");
        Interlocked.Increment(ref attachmentGeneration);
        try
        {
            target = new WindowsPresenceRenderTarget(parent.Handle);
            worker = new PresenceRenderWorker(target, DispatchFrame, Fail, logger);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            target?.Dispose();
            target = null;
            Fail(exception);
            return;
        }
        topLevel.ScalingChanged += OnHostScalingChanged;
        if (topLevel is Window window) { window.PositionChanged += OnHostPositionChanged; }
        foreach (var ancestor in this.GetVisualAncestors())
        {
            ancestors.Add(ancestor);
            ancestor.PropertyChanged += OnAncestorPropertyChanged;
        }
        LayoutUpdated += OnLayoutUpdated;
        playbackTimer.Interval = TimeSpan.FromMilliseconds(16);
        playbackTimer.Tick += OnPlaybackTick;
        Publish();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ReleaseNativeResources();
        base.OnDetachedFromVisualTree(e);
    }

    private void DispatchFrame(Action action)
    {
        var generation = Volatile.Read(ref attachmentGeneration);
        Dispatcher.UIThread.Post(() =>
        {
            if (disposed || generation != Volatile.Read(ref attachmentGeneration)) { return; }
            try
            {
                if (!CanPresent || capture() is null)
                {
                    worker?.Suspend();
                }
                action();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Fail(exception);
            }
        }, DispatcherPriority.Render);
    }

    private void OnHostScalingChanged(object? sender, EventArgs args) => Publish();
    private void OnHostPositionChanged(object? sender, PixelPointEventArgs args)
    {
        if (positionUpdatePending) { return; }
        positionUpdatePending = true;
        var generation = attachmentGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != attachmentGeneration) { return; }
            positionUpdatePending = false;
            Publish();
        }, DispatcherPriority.Render);
    }

    private void OnAncestorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == IsVisibleProperty || change.Property == OpacityProperty
            || change.Property == BoundsProperty || change.Property == Window.WindowStateProperty)
        {
            Publish();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty || change.Property == OpacityProperty
            || change.Property == BoundsProperty)
        {
            Publish();
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs args) => Publish();

    public void Refresh(bool sampleSpeech)
    {
        if (disposed || failed)
        {
            return;
        }
        if (CanPresent && sampleSpeech && CanSample(capture()))
        {
            playbackTimer.Start();
        }
        else
        {
            playbackTimer.Stop();
        }
        Publish();
    }

    public void Suspend()
    {
        playbackTimer.Stop();
        worker?.Suspend();
    }

    private void OnPlaybackTick(object? sender, EventArgs args)
    {
        try
        {
            if (!CanPresent || !CanSample(capture()))
            {
                Suspend();
                return;
            }
            samplePlayback();
            Publish();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Fail(exception);
        }
    }

    private bool CanSample(PresenceRenderInputs? inputs) => inputs is not null
        && (inputs.IsPresenceVisible && inputs.State != AssistantState.Hidden || worker?.HasVisibleFrame == true);

    private void Publish()
    {
        if (worker is null || disposed || failed)
        {
            return;
        }
        try
        {
            var inputs = capture();
            if (!CanPresent || inputs is null)
            {
                Suspend();
            }
            else
            {
                if (topLevel!.ActualTransparencyLevel != WindowTransparencyLevel.Transparent)
                {
                    throw new InvalidOperationException("The native presence requires a transparent non-layered host backend.");
                }
                var origin = this.TranslatePoint(default, topLevel!)
                    ?? throw new InvalidOperationException("The native presence has no host-relative position.");
                target!.Move(checked((int)Math.Round(origin.X * topLevel!.RenderScaling)),
                    checked((int)Math.Round(origin.Y * topLevel.RenderScaling)));
                worker.Update(inputs);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Fail(exception);
        }
    }

    private void Fail(Exception exception)
    {
        if (failed || disposed)
        {
            return;
        }
        failed = true;
        Suspend();
        reportFailure(exception);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        ReleaseNativeResources();
        disposed = true;
    }

    private void ReleaseNativeResources()
    {
        Interlocked.Increment(ref attachmentGeneration);
        positionUpdatePending = false;
        playbackTimer.Stop();
        playbackTimer.Tick -= OnPlaybackTick;
        LayoutUpdated -= OnLayoutUpdated;
        foreach (var ancestor in ancestors) { ancestor.PropertyChanged -= OnAncestorPropertyChanged; }
        ancestors.Clear();
        if (topLevel is not null) { topLevel.ScalingChanged -= OnHostScalingChanged; }
        if (topLevel is Window window) { window.PositionChanged -= OnHostPositionChanged; }
        topLevel = null;
        worker?.Dispose();
        target?.Dispose();
        releasedFrames += target?.FramesPresented ?? 0;
        worker = null;
        target = null;
    }
}