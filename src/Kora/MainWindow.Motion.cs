using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

public sealed partial class MainWindow
{
    private void ReportMotionPreferenceFailure(Exception exception) =>
        HostRequestRunner.Run(RequestOrigin.HostSystem, () =>
        {
            windowActionFailed = true;
            DesktopLog.Error(logger, exception, "Following the Windows animation preference");
            HidePresenceWindow();
            viewModel.ReportPresenceRendererStatus("Animation preference unavailable. Choose another animation mode in Settings or restart Kora.");
            viewModel.ReportHostInteractionFailure(
                "The Windows animation preference is unavailable. The presence was hidden rather than enabling unknown motion. "
                + "Choose another animation mode in Settings or restart Kora. Other presentation and audio services remain independent.");
        }, HostActivityLayer.Desktop, HostOperation.Presentation);
}
