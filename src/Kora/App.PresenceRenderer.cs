using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

public sealed partial class App
{
    private bool rendererTimingFirstTick;
    private Window GetPresenceOwner() =>
        presenceWindows?.IsAvailable == true
            && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner }
                ? owner : throw new InvalidOperationException("The presence window owner is unavailable.");

    private void OnPresenceRendererTick(object? sender, EventArgs args)
    {
        if (presenceWindows is null || viewModel is null || logger is null)
        {
            presenceRendererTimer.Stop();
            return;
        }
        HostActivity? activity = null;
        try
        {
            if (rendererTimingFirstTick)
            {
                rendererTimingFirstTick = false;
                PresenceSwitchTiming.Mark(PresenceSwitchPhase.FirstTimerTick);
            }
            if (!presenceWindows.IsReadyToApply) { return; }
            activity = (rendererSwitchContext ?? HostActivity.CaptureContinuation(
                HostActivityLayer.Desktop, HostOperation.Presentation, RequestOrigin.LocalUi))();
            if (!presenceWindows.TryApply())
            {
                activity.Complete(HostOperationOutcome.Completed);
                return;
            }
            if (viewModel is null)
            {
                activity.Complete(HostOperationOutcome.Cancelled);
                return;
            }
            if (!presenceWindows.HasPendingChange)
            {
                presenceRendererTimer.Stop();
                rendererSwitchContext = null;
            }
            viewModel.ReportPresenceRendererStatus(presenceWindows.HasPendingChange
                ? "Renderer changed; a newer saved choice is waiting to be applied."
                : presenceWindows.ActiveNative ? "Native (experimental) renderer active." : "Avalonia renderer active.");
            RendererApplied(logger, presenceWindows.ActiveNative);
            PresenceSwitchTiming.Mark(PresenceSwitchPhase.ReplacementReturned);
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            activity ??= (rendererSwitchContext ?? HostActivity.CaptureContinuation(
                HostActivityLayer.Desktop, HostOperation.Presentation, RequestOrigin.LocalUi))();
            presenceRendererTimer.Stop();
            rendererSwitchContext = null;
            RendererSwitchFailed(logger, exception);
            activity.Complete(HostOperationOutcome.Failed);
            const string message = "The saved renderer could not be applied. No automatic fallback was performed. "
                + "Select the other renderer to make a fresh attempt. If retirement failed, exit Kora normally.";
            viewModel.ReportPresenceRendererStatus(message);
            viewModel.ReportHostInteractionFailure(message);
        }
        finally
        {
            activity?.Dispose();
        }
    }
}
