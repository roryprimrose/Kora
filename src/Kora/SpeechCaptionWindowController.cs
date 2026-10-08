using Avalonia;
using Avalonia.Threading;
using Kora.Application.ViewModels;
using Kora.Core.Voice;

namespace Kora;

public sealed class SpeechCaptionWindowController : IDisposable
{
    private readonly MainViewModel viewModel;
    private readonly DispatcherTimer timer;
    private SpeechCaptionWindow? window;
    private bool disposed;

    public SpeechCaptionWindowController(MainViewModel viewModel)
    {
        this.viewModel = viewModel;
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Input, OnFrame);
        viewModel.PrivacyClosureRequested += OnPrivacyClosure;
        timer.Start();
    }

    private void OnFrame(object? sender, EventArgs args)
    {
        if (disposed) { return; }
        try { Refresh(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            timer.Stop();
            window?.Hide();
            viewModel.ReportSpeechCaptionPresentationFailure(exception.GetType().Name);
        }
    }

    private void Refresh()
    {
        viewModel.RefreshSpeechPlaybackFrame();
        if (!viewModel.IsSpeechCaptionVisible)
        {
            window?.Hide();
            return;
        }
        window ??= new SpeechCaptionWindow(viewModel);
        if (!window.IsVisible) { window.Show(); }
        var screen = window.Screens.Primary ?? throw new InvalidOperationException("The caption working area is unavailable.");
        var placement = viewModel.SpeechCaptionPlacement ?? throw new InvalidOperationException("Caption placement is unconfirmed.");
        var margin = (int)(24 * screen.Scaling);
        window.Position = new PixelPoint(
            placement is SpeechCaptionPlacement.TopLeft or SpeechCaptionPlacement.BottomLeft
                ? screen.WorkingArea.X + margin
                : Math.Max(screen.WorkingArea.X + margin, screen.WorkingArea.Right - (int)(window.Bounds.Width * screen.Scaling) - margin),
            placement is SpeechCaptionPlacement.TopLeft or SpeechCaptionPlacement.TopRight
                ? screen.WorkingArea.Y + margin
                : Math.Max(screen.WorkingArea.Y + margin, screen.WorkingArea.Bottom - (int)(window.Bounds.Height * screen.Scaling) - margin));
    }

    private void OnPrivacyClosure(object? sender, EventArgs args) => Dispatcher.UIThread.Post(() => window?.Hide());

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        timer.Stop();
        timer.Tick -= OnFrame;
        viewModel.PrivacyClosureRequested -= OnPrivacyClosure;
        if (window is not null)
        {
            window.DataContext = null;
            window.Close();
            window = null;
        }
    }
}
