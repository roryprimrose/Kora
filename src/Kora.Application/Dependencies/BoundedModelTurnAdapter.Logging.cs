using Kora.Core.Dependencies;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Dependencies;

internal sealed partial class BoundedModelTurnAdapter
{
    [LoggerMessage(5840, LogLevel.Information, "Bounded model loop ended with {Outcome} and {Reason}.")]
    private static partial void Result(ILogger logger, ModelTurnOutcome outcome, ModelTurnReason reason);

    [LoggerMessage(5841, LogLevel.Debug,
        "Bounded model loop round {Round} has {Calls} calls, {InputBytes} input bytes and {OutputBytes} output bytes.")]
    private static partial void Progress(ILogger logger, int round, int calls, int inputBytes, int outputBytes);

    [LoggerMessage(5842, LogLevel.Error, "Bounded model loop faulted with {ExceptionType}.")]
    private static partial void Fault(ILogger logger, string exceptionType);
}
