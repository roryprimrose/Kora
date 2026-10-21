using Kora.Core.Calculations;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Tools.Calculations;

public sealed class Calculate
{
    internal CalculationResult Execute(string expression, Func<bool> admitted, CancellationToken cancellationToken)
    {
        var request = HostActivity.RequireCurrent().Request;
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = request.Origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice
                && request.InvocationId is null && admitted()
                ? DecimalCalculator.Evaluate(expression) : CalculationResult.Refused;
            if (!admitted()) { result = CalculationResult.Refused; }
            activity.Complete(result.Outcome == CalculationOutcome.Succeeded
                ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            return result;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
    }
}
