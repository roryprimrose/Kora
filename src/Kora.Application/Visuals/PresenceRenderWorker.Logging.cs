using Kora.Core;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Visuals;

public sealed partial class PresenceRenderWorker
{
    [LoggerMessage(0, LogLevel.Error, "Native presence rendering failed; the presenter is unavailable.")]
    private partial void RenderFailed(Exception exception);

    [LoggerMessage(1, LogLevel.Information,
        "Native presence transition {Transition}: admitted={Admitted}, generation={Generation}, state={State}, requestedVisible={RequestedVisible}, reducedMotion={ReducedMotion}, width={Width}, height={Height}, scaling={Scaling}, positiveOpacityPresented={PositiveOpacityPresented}.")]
    private partial void RenderTransition(int transition, bool admitted, long generation, AssistantState state,
        bool requestedVisible, bool reducedMotion, double width, double height, double scaling,
        bool positiveOpacityPresented);

    [LoggerMessage(2, LogLevel.Information,
        "Native presence transition diagnostic limit reached ({Limit}); later transitions are not recorded.")]
    private partial void RenderDiagnosticLimitReached(int limit);

    [LoggerMessage(3, LogLevel.Information,
        "Native presence worker stopped: drawn={DrawnFrames}, presented={PresentedFrames}, transitionRecords={TransitionRecords}, limited={Limited}.")]
    private partial void RenderCompleted(long drawnFrames, long presentedFrames, int transitionRecords, bool limited);
}