using Kora.Core.Clock;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Clock;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Clock;

public sealed partial class LocalClockWorkflow(
    LocalClockGetTime getTime,
    LocalClockGetDate getDate,
    LocalClockGetDateAndTime getDateAndTime,
    ILogger<LocalClockWorkflow> logger)
{
    public LocalClockResult Execute(LocalClockOperation operation, Func<bool> eligible, CancellationToken cancellationToken,
        LocalClockZone zone = LocalClockZone.HostLocal)
    {
        var request = HostActivity.RequireCurrent().Request;
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        LocalClockResult Finish(LocalClockOutcome outcome, LocalClockAnswer? answer = null)
        {
            Outcome(logger, outcome);
            activity.Complete(outcome == LocalClockOutcome.Succeeded ? HostOperationOutcome.Completed
                : outcome is LocalClockOutcome.Cancelled or LocalClockOutcome.Stale ? HostOperationOutcome.Cancelled
                : HostOperationOutcome.Failed);
            return new(outcome, answer);
        }
        if (cancellationToken.IsCancellationRequested) { return Finish(LocalClockOutcome.Cancelled); }
        if (request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice) || !eligible())
        {
            return Finish(LocalClockOutcome.Denied);
        }
        try
        {
            var answer = operation switch
            {
                LocalClockOperation.Time => getTime.Execute(zone),
                LocalClockOperation.Date => getDate.Execute(zone),
                LocalClockOperation.DateAndTime => getDateAndTime.Execute(zone),
                _ => throw new ArgumentOutOfRangeException(nameof(operation)),
            };
            if (cancellationToken.IsCancellationRequested) { return Finish(LocalClockOutcome.Cancelled); }
            return eligible() ? Finish(LocalClockOutcome.Succeeded, answer) : Finish(LocalClockOutcome.Stale);
        }
        catch (Exception exception) when (exception is InvalidTimeZoneException or TimeZoneNotFoundException
            or InvalidOperationException)
        {
            Failure(logger, exception.GetType().Name);
            return Finish(LocalClockOutcome.Unavailable);
        }
    }
}
