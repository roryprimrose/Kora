using Kora.Controls;

namespace Kora;

public sealed partial class MainWindow
{
    private AvaloniaPresencePresenter? avaloniaPresence;
    private bool avaloniaPresenceFailed;
    internal nint PresenceDrawingHandle => nativePresence?.NativeHandle ?? avaloniaPresence?.Handle ?? 0;

    private void InitializeAvaloniaPresence()
    {
        if (useNativePresence) { return; }
        avaloniaPresence = new(this, presence,
            () => !retiring && !resourcesDisposed && viewModel.CanRevealPrivatePresentation
                && viewModel.IsPresenceDisplayEnabled,
            ReportAvaloniaPresenceFailure);
        viewModel.PrivacyClosureRequested += OnAvaloniaPrivacyClosure;
    }

    private void OnAvaloniaPrivacyClosure(object? sender, EventArgs args)
    {
        avaloniaPresence?.Suspend();
        RefreshPresenceInput();
    }

    private void ReportAvaloniaPresenceFailure(Exception exception)
    {
        avaloniaPresenceFailed = true;
        DesktopLog.Error(logger, exception, "Presenting the standard presence");
        HidePresenceWindow();
        viewModel.ReportPresenceRendererStatus(
            "Standard animation mode is unavailable. Choose Experimental in Settings or exit normally; Kora did not switch automatically.");
        viewModel.ReportHostInteractionFailure(
            "The standard presence renderer is unavailable. Other presentation and audio services remain independent. "
            + "Choose another animation mode in Settings or exit Kora normally.");
    }

    private void CloseAvaloniaPresence()
    {
        viewModel.PrivacyClosureRequested -= OnAvaloniaPrivacyClosure;
        avaloniaPresence?.Dispose();
    }
}
