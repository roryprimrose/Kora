using Kora.Core.Dependencies;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Dependencies;

public sealed partial class ModelProviderHandoffWorkflow
{
    [LoggerMessage(1, LogLevel.Information, "Model handoff ended with {Outcome} and {Reason}.")]
    private static partial void Result(ILogger logger, ModelHandoffOutcome outcome, ModelTurnReason reason);

    [LoggerMessage(2, LogLevel.Error, "Model handoff failed with {ExceptionType}.")]
    private static partial void Fault(ILogger logger, string exceptionType);
}
