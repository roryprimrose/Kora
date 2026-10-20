using Kora.Core.Conversions;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Tools.Conversions;

public sealed class ConvertUnits
{
    public const string ActionId = "units.convert";

    internal ConversionResult Execute(ConvertCommand command, Func<bool> admitted, CancellationToken cancellationToken)
    {
        using var activity = HostActivity.BeginChild(HostActivityLayer.Core, HostOperation.Tool);
        ConversionResult result;
        if (cancellationToken.IsCancellationRequested) { result = new(ConversionOutcome.Cancelled); }
        else if (activity.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice) || !admitted())
        { result = new(ConversionOutcome.Denied); }
        else if (command.Value is not { } value || command.From is not { } from || command.To is not { } to)
        { result = new(command.Error == ConversionOutcome.UnknownUnit ? ConversionOutcome.UnknownUnit : ConversionOutcome.InvalidInput); }
        else { result = UnitConversion.Convert(value, from, to); }
        activity.Complete(result.Outcome == ConversionOutcome.Succeeded
            ? HostOperationOutcome.Completed
            : result.Outcome == ConversionOutcome.Cancelled ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        return result;
    }
}
