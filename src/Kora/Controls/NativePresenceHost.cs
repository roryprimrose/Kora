using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

using Kora.Application.Visuals;
using Kora.Core;
using Kora.Windows.Presentation;

using Microsoft.Extensions.Logging;

namespace Kora.Controls;

internal sealed class NativePresenceHost(
    Func<PresenceRenderInputs?> capture,
    Action samplePlayback,
    Action<Exception> reportFailure,
    ILogger<PresenceRenderWorker> logger) : NativeControlHost, IDisposable
{
    private readonly DispatcherTimer playbackTimer = new();
    private WindowsPresenceRenderTarget? target;
    private PresenceRenderWorker? worker;
    private bool failed;
    private bool disposed;
    internal long FramesPresented => target?.FramesPresented ?? 0;
    internal bool CanInteract => !disposed && !failed && worker?.HasVisibleFrame == true
        && capture() is { IsPresenceVisible: true, State: not AssistantState.Hidden };

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        target = new WindowsPresenceRenderTarget(parent.Handle);
        try
        {
            worker = new PresenceRenderWorker(target, DispatchFrame, Fail, logger);
        }
        catch
        {
            target.Dispose();
            throw;
        }
        playbackTimer.Interval = TimeSpan.FromMilliseconds(16);
        playbackTimer.Tick += OnPlaybackTick;
        Publish();
        return new PlatformHandle(target.Handle, "HWND");
    }

    private void DispatchFrame(Action action) => Dispatcher.UIThread.Post(() =>
    {
        try
        {
            if (!disposed && capture() is null)
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

    protected override void DestroyNativeControlCore(IPlatformHandle control) => Dispose();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            Publish();
        }
    }

    public void Refresh(bool sampleSpeech)
    {
        if (disposed || failed)
        {
            return;
        }
        if (sampleSpeech && CanSample(capture()))
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
            if (!CanSample(capture()))
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
            if (inputs is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            {
                Suspend();
            }
            else
            {
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
        playbackTimer.Stop();
        playbackTimer.Tick -= OnPlaybackTick;
        worker?.Dispose();
        target?.Dispose();
        disposed = true;
    }
}