using Kora.Core.Commands;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Interaction;

public sealed partial class CountdownWorkflow
{
    [LoggerMessage(8920, LogLevel.Information, "Local countdown {Operation} outcome {Outcome}")]
    private static partial void Outcome(ILogger logger, CountdownOperation operation, string outcome);
}
