using System.Globalization;
using Kora.Core.Calendar;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Tools;
using Kora.Tools.Calendar;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Calendar;

public sealed partial class DateArithmeticRegistry(
    ICapabilityHostAccess host, DateDifference difference, DateAdd add, DateHelp help, ILogger<DateArithmeticRegistry> logger)
{
    public string Invoke(DateArithmeticCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        var current = HostActivity.RequireCurrent();
        if (!host.IsCurrentHost || current.Outcome != HostOperationOutcome.Unknown
            || current.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        {
            Result(logger, command.Operation, "denied");
            throw new InvalidOperationException("Date arithmetic requires a live original user request in the current host.");
        }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        try
        {
            var result = command.Operation switch
            {
                DateArithmeticOperation.Help => DateArithmeticCommand.Syntax + " " + help.Execute(),
                DateArithmeticOperation.Difference => difference.Execute(command.Start, command.End).ToString(CultureInfo.InvariantCulture)
                    + " whole calendar days (signed end minus start; start excluded, end arithmetic; not inclusive or business days).",
                DateArithmeticOperation.Add => CalendarDateArithmetic.Format(add.Execute(command.Start, command.Days)),
                _ => throw new InvalidDataException("Invalid date operation. " + DateArithmeticCommand.Syntax),
            };
            activity.Complete(HostOperationOutcome.Completed);
            Result(logger, command.Operation, "completed");
            return result;
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            Result(logger, command.Operation, "failed");
            throw;
        }
    }
}
