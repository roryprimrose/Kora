using Avalonia;
using Avalonia.Threading;
using Kora.Application.ViewModels;

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
        if (window.IsVisible) { return; }
        window.Show();
        var screen = window.Screens.Primary;
        if (screen is not null)
        {
            var margin = (int)(24 * screen.Scaling);
            window.Position = new PixelPoint(
                Math.Max(screen.WorkingArea.X + margin, screen.WorkingArea.Right - (int)(window.Bounds.Width * screen.Scaling) - margin),
                Math.Max(screen.WorkingArea.Y + margin, screen.WorkingArea.Bottom - (int)(window.Bounds.Height * screen.Scaling) - margin));
        }
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
