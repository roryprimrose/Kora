using Kora.Core.Dependencies;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Dependencies;

public sealed partial class LocalContextTurnWorkflow
{
    [LoggerMessage(1, LogLevel.Warning, "Local context lifecycle evidence failed with {ExceptionType}; no retry is authorized.")]
    private static partial void Fault(ILogger logger, string exceptionType);

    [LoggerMessage(2, LogLevel.Information, "Local context completed with {Outcome}, {Reason}, durable state {DurableState}.")]
    private static partial void Result(ILogger logger, ModelTurnOutcome outcome, LocalContextTurnReason reason, HostTaskState? durableState);
}